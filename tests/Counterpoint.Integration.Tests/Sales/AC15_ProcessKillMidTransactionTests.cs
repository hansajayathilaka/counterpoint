using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.SeedGenerator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// <b>AC-15</b> - "A simulated power cut mid-bill leaves the database uncorrupted and the last
/// committed bill intact", proved by killing a real OS process a hundred times, at a hundred
/// different random points in its trading loop, and requiring the database to be intact every
/// single time.
/// </summary>
/// <remarks>
/// <para>
/// A real process, a real <c>SIGKILL</c> (<see cref="Process.Kill()"/> on Linux), not an
/// in-process rollback: what this proves is that SQLite's WAL recovery under
/// <c>synchronous=FULL</c> (CLAUDE.md invariant 9) genuinely survives an abrupt process death at
/// an unpredictable point, which an in-process "throw and catch" test cannot exercise - the OS
/// never actually stops writing under the calling process's feet in that version. A physical
/// power cut to the shop terminal itself is a different failure mode again (page-cache loss the
/// OS never gets a chance to flush) and is <c>HW-T09</c>'s job, on real hardware.
/// </para>
/// <para>
/// The killed process is <c>Counterpoint.SeedGenerator</c>'s <c>--sell-loop</c> mode
/// (tools/SeedGenerator/Program.cs): it signs in, resolves the seeded shift and product, and
/// completes one real <see cref="Counterpoint.Application.Sales.ICompleteSale"/> bill after
/// another as fast as it can, printing <c>SOLD &lt;bill_no&gt;</c> after each commit. One
/// uninitialised, uninterrupted priming run establishes the schema and the first committed bill
/// first (AC-15 is about a power cut mid-<em>bill</em>, not mid-<em>migration</em>); the hundred
/// kills that follow all land against an already-trading till.
/// </para>
/// <para>
/// After every kill: <c>PRAGMA integrity_check</c> must report <c>ok</c>, the sale and audit_log
/// hash chains (<see cref="HashChainVerifier"/>, the same command <c>/perf-gate</c> and
/// <c>scripts/seed.sh</c> use) must be unbroken from genesis, and the bill numbers actually
/// committed must form a gapless run from 1 - exactly what CLAUDE.md invariant 4 promises even
/// when the till dies mid-sentence.
/// </para>
/// </remarks>
public sealed class AC15_ProcessKillMidTransactionTests
{
    private const int KillCount = 100;
    private const int SalesPerRun = 400;

    [Fact]
    public async Task AC_15_AHundredProcessKillsAtRandomPointsLeaveTheDatabaseIntactEveryTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "counterpoint-ac15", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // Priming run: uninterrupted, so the schema and the seeded shift/product exist before
            // the first kill. AC-15 is about a power cut mid-bill, not mid-migration.
            var priming = await RunSellLoopAsync(root, salesToAttempt: 1, killAfterMs: null);
            priming.ExitCode.Should().Be(0, "the priming run must complete cleanly: " + priming.Output);
            priming.Output.Should().Contain("DONE");

            var random = new Random(20260910);

            for (var iteration = 0; iteration < KillCount; iteration++)
            {
                var killAfterMs = random.Next(0, 120);

                var run = await RunSellLoopAsync(root, SalesPerRun, killAfterMs);

                // The process must actually have been killed (or, rarely, have raced past
                // SalesPerRun sales before the delay elapsed) - never have crashed on its own for
                // some other reason, which would not be testing AC-15 at all.
                run.WasKilled.Should().BeTrue(
                    $"iteration {iteration}: the process should have been killed, not exited on its own (exit {run.ExitCode}): {run.Output}");

                await AssertDatabaseIsIntactAsync(root, iteration);
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>Reopens the database exactly as the composition root would and asserts three things: the file itself is not corrupt, both hash chains are unbroken from genesis, and the bill numbers committed so far are gapless from 1.</summary>
    private static async Task AssertDatabaseIsIntactAsync(string root, int iteration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddCounterpointInfrastructure(root);
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();
        var connectionFactory = provider.GetRequiredService<IPosConnectionFactory>();

        await using (var connection = await connectionFactory.OpenReadConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = (string)(await command.ExecuteScalarAsync())!;
            result.Should().Be("ok", $"iteration {iteration}: PRAGMA integrity_check must report the database intact");
        }

        var saleChain = await HashChainVerifier.VerifySaleChainAsync(connectionFactory);
        saleChain.IsIntact.Should().BeTrue(
            $"iteration {iteration}: sale hash chain broke at row {saleChain.FirstBrokenRowId}");

        var auditChain = await HashChainVerifier.VerifyAuditLogChainAsync(connectionFactory);
        auditChain.IsIntact.Should().BeTrue(
            $"iteration {iteration}: audit_log hash chain broke at row {auditChain.FirstBrokenRowId}");

        await using (var connection = await connectionFactory.OpenReadConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT bill_no FROM sale WHERE bill_no LIKE 'INV-%' ORDER BY id;";
            await using var reader = await command.ExecuteReaderAsync();

            var expectedNext = 1L;
            while (await reader.ReadAsync())
            {
                var billNo = reader.GetString(0);
                var sequence = long.Parse(billNo.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture);

                sequence.Should().Be(
                    expectedNext,
                    $"iteration {iteration}: bill numbering must be gapless even across a kill - expected {expectedNext}, found {sequence} ({billNo})");

                expectedNext++;
            }

            expectedNext.Should().BeGreaterThan(1, $"iteration {iteration}: at least the priming bill must always be present");
        }
    }

    /// <summary>Runs the sell-loop tool once, optionally killing it <paramref name="killAfterMs"/> after it prints READY.</summary>
    private static async Task<SellLoopRun> RunSellLoopAsync(string root, int salesToAttempt, int? killAfterMs)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Counterpoint.SeedGenerator.dll");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                dllPath,
                "--sell-loop",
                "--db-root", root,
                "--count", salesToAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(args.Data);
            }

            if (args.Data == "READY")
            {
                readyTcs.TrySetResult();
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(args.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var wasKilled = false;

        if (killAfterMs is { } delay)
        {
            var readyOrExit = await Task.WhenAny(readyTcs.Task, WaitForExitAsync(process), Task.Delay(TimeSpan.FromSeconds(10)));

            if (readyOrExit == readyTcs.Task)
            {
                await Task.Delay(delay);
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                wasKilled = true;
            }
        }

        await WaitForExitAsync(process);

        string capturedOutput;
        lock (output)
        {
            capturedOutput = output.ToString();
        }

        return new SellLoopRun(process.ExitCode, wasKilled, capturedOutput);
    }

    private static Task WaitForExitAsync(Process process)
    {
        if (process.HasExited)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult();

        if (process.HasExited)
        {
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SellLoopRun(int ExitCode, bool WasKilled, string Output);
}

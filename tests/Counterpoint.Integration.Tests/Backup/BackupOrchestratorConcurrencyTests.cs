using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P1-T15 done-when item 2: a backup taken while a sale is being rung up does not delay the sale,
/// measured. <see cref="SnapshotConcurrencyTests"/> already proves - deadlock-freely - that
/// <c>SqliteDatabaseSnapshotSource.CreateRawCopyAsync</c> never reaches for the write-gated
/// connection a sale needs; this test drives the same gate through the real, composed
/// <see cref="SnapshotService.CreateSnapshotAsync"/> this task hands to <c>BackupOrchestrator</c>
/// and <c>BackupScheduler</c>, and adds an actual elapsed-time assertion on the sale rather than
/// only a no-deadlock race, which is what "measured" in the task's own done-when list asks for.
/// The compress and encrypt steps that follow <c>CreateRawCopyAsync</c> never open a database
/// connection at all, so they cannot contend for one by construction; what is worth proving here
/// is that the moment before them - opening the read connection <c>VACUUM INTO</c> needs - never
/// stalls a sale either, even while held open for the whole rest of the pipeline's duration.
/// </summary>
public sealed class BackupOrchestratorConcurrencyTests
{
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    private const string Passphrase = "correct horse battery staple";

    /// <summary>
    /// A generous safety-net budget for how long a sale may take while a snapshot is deliberately
    /// held in flight - the point is not to pin down an exact millisecond figure (that is
    /// <c>HW-T07</c>'s job, on the shop's own terminal), but to prove the sale is not waiting on
    /// the snapshot at all: it should complete about as fast as it would with no backup running.
    /// </summary>
    private static readonly TimeSpan SaleBudget = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ASaleCompletesWellWithinBudgetWhileTheFullBackupPipelineIsStillInFlight()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var gated = new GatedReadConnectionFactory(fixture.Resolve<IPosConnectionFactory>());
        var snapshotSource = new SqliteDatabaseSnapshotSource(gated, fixture.Resolve<IDatabaseKeyStore>());

        var gatedSnapshotService = new SnapshotService(
            snapshotSource,
            fixture.Resolve<IBackupPassphraseStore>(),
            fixture.Resolve<IBackupRecordStore>(),
            fixture.Resolve<SnapshotOptions>(),
            fixture.Resolve<Argon2Parameters>(),
            fixture.Resolve<TimeProvider>());

        var snapshotTask = gatedSnapshotService.CreateSnapshotAsync();

        var readRequested = await Task.WhenAny(gated.ReadRequested, Task.Delay(TimeSpan.FromSeconds(5)));
        readRequested.Should().BeSameAs(gated.ReadRequested, "the snapshot pipeline must actually be in flight before the race below means anything");

        var stopwatch = Stopwatch.StartNew();
        var saleTask = CompleteOneAsync(fixture);

        var winner = await Task.WhenAny(saleTask, Task.Delay(SaleBudget * 5));
        winner.Should().BeSameAs(saleTask, "a sale must never wait on a snapshot still in flight (CLAUDE.md invariant 7)");

        var completed = await saleTask;
        stopwatch.Stop();

        completed.SaleId.Should().BeGreaterThan(0);
        stopwatch.Elapsed.Should().BeLessThan(
            SaleBudget,
            "the sale ran while the backup pipeline's read connection was still deliberately held open - "
                + "it must finish in about the time an ordinary sale takes, not wait on the backup at all");

        gated.ReleaseRead();
        var snapshot = await snapshotTask;
        snapshot.SizeBytes.Should().BeGreaterThan(0);
    }

    private static async Task<CompletedSale> CompleteOneAsync(SaleFixture fixture)
    {
        var lines = new List<SaleLineRequest> { new(await SeededVariantIdAsync(fixture), 1m) };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await SeededUserIdAsync(fixture),
                await SeededShiftIdAsync(fixture),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

    /// <summary>
    /// Delegates every call straight through except <see cref="OpenReadConnectionAsync"/>, which
    /// signals <see cref="ReadRequested"/> and then blocks until the test calls
    /// <see cref="ReleaseRead"/> - the same fake <c>SnapshotConcurrencyTests</c> uses, duplicated
    /// here rather than shared, because that class keeps it private to its own file.
    /// </summary>
    private sealed class GatedReadConnectionFactory : IPosConnectionFactory
    {
        private readonly IPosConnectionFactory _inner;
        private readonly TaskCompletionSource _readRequestedSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReadSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal GatedReadConnectionFactory(IPosConnectionFactory inner) => _inner = inner;

        internal Task ReadRequested => _readRequestedSource.Task;

        internal void ReleaseRead() => _releaseReadSource.TrySetResult();

        public string DatabaseFilePath => _inner.DatabaseFilePath;

        public ValueTask<WriteConnectionLease> AcquireWriteConnectionAsync(
            CancellationToken cancellationToken = default) =>
            _inner.AcquireWriteConnectionAsync(cancellationToken);

        public async Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default)
        {
            _readRequestedSource.TrySetResult();
            await _releaseReadSource.Task.ConfigureAwait(false);
            return await _inner.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        public DbConnection OpenConfiguredConnection() => _inner.OpenConfiguredConnection();
    }
}

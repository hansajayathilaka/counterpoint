using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Devices.Printing;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Counterpoint.Integration.Tests.Printing;

/// <summary>
/// The print outbox worker against the real database (SAD §8, SRS FR-7.8, AC-16).
/// </summary>
public sealed class PrintWorkerTests
{
    [Fact]
    public async Task FR_7_8_NoDatabaseTransactionIsHeldWhileThePrinterIsBusy()
    {
        await using var fixture = await Sales.SaleFixture.CreateAsync();

        var connections = fixture.Resolve<IPosConnectionFactory>();
        var outbox = fixture.Resolve<IPrintJobOutbox>();

        await outbox.EnqueueAsync(new PrintJobRequest("SALE", 1, [0x1B, 0x40]));

        // A printer that tries to take the single write lease while it is "printing". If the
        // worker were still holding a transaction open across this call, the till's one writer
        // would be locked and this would never return - which is exactly the failure mode
        // CLAUDE.md invariant 7 exists to prevent, and exactly what a slow printer causes.
        var printer = new WriteWhilePrintingPrinter(connections);

        var worker = new PrintWorker(
            outbox,
            printer,
            new PrintWorkerOptions { PollInterval = TimeSpan.FromMilliseconds(5) },
            TimeProvider.System,
            NullLogger<PrintWorker>.Instance);

        var printed = await worker.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        printed.Should().Be(1);
        printer.TookTheWriteLease.Should().BeTrue(
            "the writer must be free while the printer is working");
    }

    [Fact]
    public async Task FR_7_8_TheWorkerLoopSurvivesAnOutboxThatThrows()
    {
        var worker = new PrintWorker(
            new ThrowingOutbox(),
            new NeverCalledPrinter(),
            new PrintWorkerOptions { PollInterval = TimeSpan.FromMilliseconds(5) },
            TimeProvider.System,
            NullLogger<PrintWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        // Long enough for several failed passes.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        worker.ExecuteTask.Should().NotBeNull();
        worker.ExecuteTask!.IsFaulted.Should().BeFalse(
            "an exception escaping ExecuteAsync stops the host, which would take the sales "
            + "screen down because a receipt did not print");

        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>Prints by proving the write gate is free, then succeeding.</summary>
    private sealed class WriteWhilePrintingPrinter : IReceiptPrinter
    {
        private readonly IPosConnectionFactory _connections;

        internal WriteWhilePrintingPrinter(IPosConnectionFactory connections) => _connections = connections;

        internal bool TookTheWriteLease { get; private set; }

        public async Task<PrintOutcome> PrintAsync(
            ReadOnlyMemory<byte> document,
            string jobName,
            CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var lease = await _connections.AcquireWriteConnectionAsync(timeout.Token);
            await using (lease.ConfigureAwait(false))
            {
                TookTheWriteLease = true;
            }

            return PrintOutcome.Success("test");
        }
    }

    /// <summary>An outbox that is broken in the worst way: it throws on every call.</summary>
    private sealed class ThrowingOutbox : IPrintJobOutbox
    {
        public Task<long> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the database is unavailable");

        public Task<PendingPrintJob?> NextPendingAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the database is unavailable");

        public Task MarkPrintedAsync(long printJobId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the database is unavailable");

        public Task RecordFailedAttemptAsync(
            long printJobId,
            string failureReason,
            bool giveUp,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the database is unavailable");
    }

    /// <summary>A printer the worker never gets as far as calling.</summary>
    private sealed class NeverCalledPrinter : IReceiptPrinter
    {
        public Task<PrintOutcome> PrintAsync(
            ReadOnlyMemory<byte> document,
            string jobName,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the worker should never have reached the printer");
    }
}

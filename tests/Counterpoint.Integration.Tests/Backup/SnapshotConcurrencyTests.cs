using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P0-T07 regression coverage: <c>SqliteDatabaseSnapshotSource.CreateRawCopyAsync</c> must never
/// go through the single write-gated connection every sale-completing transaction shares
/// (<c>PosConnectionFactory.AcquireWriteConnectionAsync</c>'s <c>_writeGate</c>). A snapshot
/// holding that gate for the duration of <c>VACUUM INTO</c> would let a backup stall a customer's
/// sale - a direct violation of CLAUDE.md invariant 7, "Never block the sale". These tests use
/// <c>SqliteDatabaseSnapshotSource</c> directly (an <c>Counterpoint.Infrastructure</c>-internal
/// type, reachable here only via that assembly's <c>InternalsVisibleTo</c>) wrapped in fakes that
/// make the invariant observable deterministically, rather than by racing a wall clock.
/// </summary>
public sealed class SnapshotConcurrencyTests
{
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    /// <summary>
    /// The direct, unconditional proof: wrap the till's real connection factory in one that
    /// throws if <see cref="IPosConnectionFactory.AcquireWriteConnectionAsync"/> is ever called,
    /// and prove <c>CreateRawCopyAsync</c> still succeeds. If this regresses to the old design -
    /// acquiring the write-gated lease for <c>VACUUM INTO</c> - this test fails immediately, with
    /// no timing involved at all.
    /// </summary>
    [Fact]
    public async Task CreateRawCopyAsyncNeverAcquiresTheWriteGatedConnection()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var forbidWrites = new ForbidWriteAcquisitionConnectionFactory(fixture.Resolve<IPosConnectionFactory>());
        var snapshotSource = new SqliteDatabaseSnapshotSource(forbidWrites, fixture.Resolve<IDatabaseKeyStore>());

        var destination = Path.Combine(fixture.SnapshotDirectory, "no-write-gate-" + Guid.NewGuid().ToString("N") + ".tmp");

        var result = await snapshotSource.CreateRawCopyAsync(destination);

        result.SizeBytes.Should().BeGreaterThan(0, "VACUUM INTO must have produced a real file over the plain read connection");
        File.Exists(destination).Should().BeTrue();
    }

    /// <summary>
    /// The end-to-end proof: while a snapshot is genuinely in flight - already past the point of
    /// requesting its connection, deliberately held open by a gate the test controls - a sale
    /// completes anyway. The only wall-clock element is a generous safety-net timeout that turns
    /// a would-be deadlock (the pre-fix behaviour) into a clear test failure instead of a hang;
    /// the pass/fail outcome itself does not depend on timing.
    /// </summary>
    [Fact]
    public async Task ASaleCompletesWhileASnapshotIsStillInFlight()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var gated = new GatedReadConnectionFactory(fixture.Resolve<IPosConnectionFactory>());
        var snapshotSource = new SqliteDatabaseSnapshotSource(gated, fixture.Resolve<IDatabaseKeyStore>());

        var destination = Path.Combine(fixture.SnapshotDirectory, "in-flight-" + Guid.NewGuid().ToString("N") + ".tmp");

        var snapshotTask = snapshotSource.CreateRawCopyAsync(destination);

        // Wait until the snapshot has actually asked for its connection - i.e. it is genuinely
        // "in flight" - before racing anything against it. Bounded so a regression that never
        // calls OpenReadConnectionAsync at all (e.g. reverting to AcquireWriteConnectionAsync)
        // fails the test instead of hanging the suite.
        var readRequested = await Task.WhenAny(gated.ReadRequested, Task.Delay(TimeSpan.FromSeconds(5)));
        readRequested.Should().BeSameAs(
            gated.ReadRequested,
            "CreateRawCopyAsync must open a plain read connection to do its work");

        var saleTask = CompleteOneAsync(fixture);

        // The snapshot's read connection is still deliberately held open at this point - gated.
        // ReleaseRead() below has not been called yet. If completing a sale needed that
        // connection, or needed the write gate the old buggy code held for the vacuum's
        // duration, saleTask could not finish here.
        var winner = await Task.WhenAny(saleTask, Task.Delay(TimeSpan.FromSeconds(5)));
        winner.Should().BeSameAs(
            saleTask,
            "a sale must not wait on a snapshot still in flight (CLAUDE.md invariant 7, 'Never block the sale')");

        var completed = await saleTask;
        completed.SaleId.Should().BeGreaterThan(0);

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
    /// Delegates every call straight through except <see cref="AcquireWriteConnectionAsync"/>,
    /// which throws - so any code path that reaches for the write-gated connection is caught the
    /// instant it tries, rather than by observing a stall.
    /// </summary>
    private sealed class ForbidWriteAcquisitionConnectionFactory : IPosConnectionFactory
    {
        private readonly IPosConnectionFactory _inner;

        internal ForbidWriteAcquisitionConnectionFactory(IPosConnectionFactory inner) => _inner = inner;

        public string DatabaseFilePath => _inner.DatabaseFilePath;

        public ValueTask<WriteConnectionLease> AcquireWriteConnectionAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "CreateRawCopyAsync must not acquire the write-gated connection - VACUUM INTO only "
                + "reads the source database (CLAUDE.md invariant 7, 'Never block the sale').");

        public Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default) =>
            _inner.OpenReadConnectionAsync(cancellationToken);

        public DbConnection OpenConfiguredConnection() => _inner.OpenConfiguredConnection();
    }

    /// <summary>
    /// Delegates every call straight through except <see cref="OpenReadConnectionAsync"/>, which
    /// signals <see cref="ReadRequested"/> and then blocks until the test calls
    /// <see cref="ReleaseRead"/> - simulating a snapshot's read connection being held open for the
    /// duration of a slow <c>VACUUM INTO</c>, deterministically, without actually needing a slow
    /// database.
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

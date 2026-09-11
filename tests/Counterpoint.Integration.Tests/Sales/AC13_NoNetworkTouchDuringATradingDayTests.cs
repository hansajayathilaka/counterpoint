using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// <b>AC-13</b> - "The system operates for a full simulated trading day with the network cable
/// unplugged, with zero functional loss."
/// </summary>
/// <remarks>
/// <para>
/// This is the software half: proved in-process, against the Linux fakes, by intercepting every
/// socket connection and DNS resolution the process makes - not by literally unplugging a cable,
/// which a CI container has no cable to unplug in the first place (docs/09_HARDWARE_INTEGRATION.md
/// HW-T09 runs the physical cable-out trading day, on real hardware, later). A cashier's day is
/// simulated end to end - sign-in, barcode scan, counter search, several completed bills
/// (including a split tender and a cancellation), a held-and-recalled bill, a stock enquiry and a
/// local backup snapshot - and none of it may open a socket or resolve a name, because none of it
/// is supposed to (CLAUDE.md: "No code path in a sale, return, price lookup or report may touch
/// the network").
/// </para>
/// <para>
/// <see cref="NetworkActivityGuard"/> is proved against a real socket connection first
/// (<see cref="AC_13_TheNetworkGuardItselfDetectsARealSocketConnection"/>): a harness that could
/// never fire is not a control, it is decoration.
/// </para>
/// </remarks>
public sealed class AC13_NoNetworkTouchDuringATradingDayTests
{
    private static readonly DateTimeOffset TradingDayStart =
        new(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_13_TheNetworkGuardItselfDetectsARealSocketConnection()
    {
        using var guard = new NetworkActivityGuard();

        // TEST-NET-1 (RFC 5737): guaranteed non-routable, so the connection attempt itself - not
        // any response - is what this proves. A short timeout keeps the positive control fast.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var connect = socket.ConnectAsync(IPAddress.Parse("192.0.2.1"), 65000);
            await Task.WhenAny(connect, Task.Delay(TimeSpan.FromMilliseconds(500)));
        }
        catch
        {
            // The connection failing or timing out is expected and irrelevant - only the attempt
            // matters here.
        }

        guard.Events.Should().NotBeEmpty(
            "the guard must be able to see a real socket connection attempt, or its silence "
            + "below would prove nothing");
    }

    [Fact]
    public async Task AC_13_ASimulatedTradingDayNeverTouchesTheNetwork()
    {
        using var guard = new NetworkActivityGuard();

        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        // Barcode scan (SRS FR-3.1, NFR-P1).
        var scan = fixture.Resolve<IScanItem>();
        var scanned = await scan.ScanAsync(FirstRunSeeder.SeededBarcode);
        scanned.Should().NotBeNull("the seeded barcode must resolve for the rest of the day to have anything to sell");

        // Counter search (SRS FR-2.11, NFR-P2).
        var search = fixture.Resolve<IProductSearchService>();
        await search.SearchAsync("Galvanised");

        // A plain one-line cash sale.
        var quoteSale = fixture.Resolve<IQuoteSale>();
        var completeSale = fixture.Resolve<ICompleteSale>();
        var userId = fixture.Resolve<Counterpoint.Application.Security.ISession>().CurrentUser!.Id;
        var shiftId = await SeededOpenShiftIdAsync(fixture);

        var firstQuote = await quoteSale.QuoteAsync([new SaleLineRequest(scanned!.ProductVariantId, 2m)]);
        var firstSale = await completeSale.CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, TradingDayStart,
            [new SaleLineRequest(scanned.ProductVariantId, 2m)],
            [new TenderRequest(TenderTypes.Cash, firstQuote.Total)]));

        // A split-tender bill (SRS FR-3.24-FR-3.26, part of AC-02's shape).
        var secondQuote = await quoteSale.QuoteAsync([new SaleLineRequest(scanned.ProductVariantId, 1m)]);
        var half = Money.FromScaled(secondQuote.Total.ToScaled() / 2);
        await completeSale.CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, TradingDayStart.AddMinutes(5),
            [new SaleLineRequest(scanned.ProductVariantId, 1m)],
            [
                new TenderRequest(TenderTypes.Cash, half),
                new TenderRequest(TenderTypes.Card, secondQuote.Total - half, "AUTH-000001"),
            ]));

        // A hold and a recall (SRS FR-3.32, FR-3.33).
        var heldBills = fixture.Resolve<IHeldBillService>();
        var heldId = await heldBills.HoldAsync(
            "Customer at the counter",
            new HeldBillPayload(null, false, null, [new HeldBillLinePayload(scanned.ProductVariantId, 1m, null, null, null, false, null)]),
            userId);
        await heldBills.RecallAsync(heldId);

        // A bill rung up and then cancelled the same business day (SRS FR-3.34).
        var thirdQuote = await quoteSale.QuoteAsync([new SaleLineRequest(scanned.ProductVariantId, 1m)]);
        var thirdSale = await completeSale.CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, TradingDayStart.AddMinutes(10),
            [new SaleLineRequest(scanned.ProductVariantId, 1m)],
            [new TenderRequest(TenderTypes.Cash, thirdQuote.Total)]));

        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(thirdSale.SaleId, "Customer changed their mind", TradingDayStart.AddMinutes(11)));

        // A stock enquiry (SRS FR-4).
        await fixture.Resolve<IStockEnquiry>().FindByVariantIdAsync(scanned.ProductVariantId);

        // A local backup snapshot, taken manually - never a cloud upload, which does not exist
        // until Phase 4 (CLAUDE.md "Not cloud-dependent").
        var backupOutcome = await fixture.Resolve<IManualBackupTrigger>().RunNowAsync();
        backupOutcome.Should().NotBeNull();

        firstSale.SaleId.Should().BeGreaterThan(0);
        guard.Events.Should().BeEmpty(
            "a full simulated trading day - scan, search, sale, split tender, hold/recall, "
            + "cancellation, stock enquiry and a local backup - must never open a socket or "
            + "resolve a name: " + string.Join(", ", guard.Events.Take(10)));
    }

    private static async Task<long> SeededOpenShiftIdAsync(SaleFixture fixture) =>
        Convert.ToInt64(await fixture.ScalarAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;"),
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Listens for every "System.Net.*" <see cref="EventSource"/> - sockets, DNS resolution, TLS,
    /// HTTP - at verbose level, and records every event any of them raises. Framework-level, not
    /// application code: it would catch a stray <see cref="System.Net.Http.HttpClient"/> call or a
    /// raw <see cref="Socket"/> connect anywhere in the process, including inside a third-party
    /// library neither this test nor the production code wrote.
    /// </summary>
    private sealed class NetworkActivityGuard : EventListener
    {
        private readonly ConcurrentBag<string> _events = new();

        public System.Collections.Generic.IReadOnlyCollection<string> Events => _events;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            // The public, documented telemetry sources for an outbound connection, a DNS
            // resolution, TLS and HTTP - not "Private.InternalDiagnostics.System.Net.*", which is
            // the runtime's own verbose per-buffer debug logging and fires for the test host's
            // unrelated IPC sockets as readily as for a real outbound connection, and so proves
            // nothing about what the code under test did.
            if (eventSource.Name.StartsWith("System.Net", StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Verbose);
            }

            base.OnEventSourceCreated(eventSource);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            _events.Add(eventData.EventSource.Name + ":" + (eventData.EventName ?? "?"));
        }
    }
}

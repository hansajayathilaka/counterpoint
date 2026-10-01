using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.Tests.Support;

/// <summary>
/// In-memory stand-ins for the eleven P3-T06 queries and the filter lookup, so a stock, tax or cash screen can be
/// rendered in a headless window with known figures and no database - and made to fail on demand. What is under
/// test in the view tests is the binding, the formatting, the navigation and the failure handling, never the
/// figures: <c>Counterpoint.Integration.Tests</c> proves those against a real SQLite file.
/// </summary>
internal static class StockCashStubs
{
    internal static Money M(decimal amount) => Money.FromDecimal(amount);

    internal static Quantity Q(decimal value) => Quantity.FromDecimal(value, 1);

    internal static readonly DateOnly Day = new(2026, 9, 6);

    internal static readonly DateTimeOffset Moment = new(2026, 9, 6, 10, 5, 0, TimeSpan.FromHours(5.5));
}

/// <summary>A query stub that can be told to throw, counts its calls and remembers what it was asked.</summary>
internal abstract class FaultableStub
{
    /// <summary>Thrown from the next call (and every one after) when set.</summary>
    internal Exception? Fault { get; set; }

    /// <summary>When set, the call waits on it before answering - so a test can look at the screen mid-run.</summary>
    internal TaskCompletionSource? Gate { get; set; }

    internal int Calls { get; private set; }

    internal ReportDateRange? LastRange { get; private set; }

    protected async Task EnterAsync(ReportDateRange? range = null)
    {
        Calls++;
        LastRange = range;

        if (Gate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        if (Fault is { } fault)
        {
            throw fault;
        }
    }
}

internal sealed class StubTaxReportQuery : FaultableStub, ITaxReportQuery
{
    internal bool Reconciled { get; set; } = true;

    internal bool PricesIncludeTax { get; set; }

    internal string ShopName { get; set; } = "Kandy Hardware";

    internal string Registration { get; set; } = "TIN-204-118-77";

    internal string Label { get; set; } = "VAT";

    internal bool Empty { get; set; }

    public async Task<TaxReport> GetTaxReportAsync(ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        var header = new TaxReportHeader(ShopName, Registration, Label, PricesIncludeTax);

        if (Empty)
        {
            return new TaxReport(range, header, [], Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, true);
        }

        var m = StockCashStubs.M;

        return new TaxReport(
            range,
            header,
            [
                new TaxReportRow(TaxReportRowKind.Rate, TaxRate.FromPercent(0m), m(300.00m), Money.Zero, Money.Zero, Money.Zero, m(300.00m), Money.Zero),
                new TaxReportRow(TaxReportRowKind.Rate, TaxRate.FromPercent(10m), m(1294.00m), m(129.40m), m(335.50m), m(33.55m), m(958.50m), m(95.85m)),
                new TaxReportRow(TaxReportRowKind.UnlinkedReturns, null, Money.Zero, Money.Zero, m(100.00m), m(10.00m), m(-100.00m), m(-10.00m)),
            ],
            m(1594.00m), m(129.40m), m(435.50m), m(1158.50m), m(43.55m), m(85.85m),
            Reconciled ? m(129.40m) : m(129.00m), m(129.40m), m(43.55m), Reconciled);
    }
}

internal sealed class StubTenderReconciliationQuery : FaultableStub, ITenderReconciliationQuery
{
    internal bool TiedOut { get; set; }

    internal bool OpenShiftListed { get; set; } = true;

    /// <summary>What the listed open shift is said to account for; the rest of the difference is unexplained.</summary>
    internal decimal ListedEffect { get; set; } = 340.95m;

    /// <summary>Lists, instead of the open shift, a closed shift whose Z report straddles the range's edge.</summary>
    internal bool DateBoundaryListed { get; set; }

    public async Task<TenderReconciliation> GetReconciliationAsync(ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        var m = StockCashStubs.M;

        IReadOnlyList<TenderNotZdItem> notZd = TiedOut
            ? []
            : DateBoundaryListed
                ? [new TenderNotZdItem(
                    1, "SH-000001", "CLOSED", new DateOnly(2026, 9, 6), 0, 0,
                    "This shift's Z report is dated 2026-09-06 but it also holds trading dated 2026-09-07, outside this range.",
                    2, 3, m(ListedEffect))]
                : OpenShiftListed
                    ? [new TenderNotZdItem(2, "SH-000002", "OPEN", new DateOnly(2026, 9, 7), 2, 3, "Not Z'd yet - this shift is still open.", 0, 0, m(ListedEffect))]
                    : [];
        var difference = TiedOut ? Money.Zero : m(340.95m);
        var unexplained = difference - notZd.Aggregate(Money.Zero, (sum, item) => sum + item.NetEffect);

        return new TenderReconciliation(
            range,
            [new TenderShiftRow(1, "SH-000001", StockCashStubs.Day, [new XReportTenderLine("CASH", m(650.00m), Money.Zero, m(650.00m))], m(650.00m))],
            [
                new TenderTieOutRow("BANK_TRANSFER", m(100.00m), Money.Zero, m(100.00m), m(100.00m), Money.Zero, m(100.00m), Money.Zero, Money.Zero),
                new TenderTieOutRow(
                    "CASH", m(650.00m), Money.Zero, m(650.00m), m(1360.00m), m(369.05m), m(990.95m), difference, unexplained),
            ],
            m(750.00m), TiedOut ? m(750.00m) : m(1090.95m), TiedOut ? Money.Zero : m(340.95m), notZd, TiedOut);
    }
}

internal sealed class StubShiftVarianceHistoryQuery : FaultableStub, IShiftVarianceHistoryQuery
{
    internal VarianceTrend Trend { get; set; } = VarianceTrend.Improving;

    public async Task<ShiftVarianceHistory> GetHistoryAsync(ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        var m = StockCashStubs.M;
        var opened = new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));

        return new ShiftVarianceHistory(
            range,
            m(500.00m),
            [
                new ShiftVarianceRow(1, "SH-000001", StockCashStubs.Day, "Shop Owner", "Shop Owner", opened.AddHours(9), m(1610.00m), m(1000.00m), m(610.00m), "Recounted twice", true, m(610.00m)),
                new ShiftVarianceRow(2, "SH-000002", StockCashStubs.Day.AddDays(1), "Shop Owner", "Shop Owner", opened.AddDays(1).AddHours(9), m(880.00m), m(1000.00m), m(-120.00m), null, false, m(490.00m)),
            ],
            m(490.00m), m(610.00m), m(-120.00m), m(365.00m), 1, Trend, m(610.00m), m(120.00m));
    }
}

internal sealed class StubStockCardQuery : FaultableStub, IStockCardQuery
{
    internal bool Unknown { get; set; }

    internal bool Broken { get; set; }

    /// <summary>A sparse range: the second row was back-dated and follows movements outside the range.</summary>
    internal bool Sparse { get; set; }

    internal string? LastKey { get; private set; }

    public async Task<StockCard?> GetStockCardAsync(long productVariantId, ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        return Unknown ? null : Build(range);
    }

    public async Task<StockCard?> GetStockCardBySkuAsync(string skuOrBarcode, ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        LastKey = skuOrBarcode;
        return Unknown ? null : Build(range);
    }

    private StockCard Build(ReportDateRange range)
    {
        var m = StockCashStubs.M;
        var q = StockCashStubs.Q;

        return new StockCard(
            7, "RPT-BOLT-A", "Bolt", "pc", range, q(1000m),
            [
                new StockCardRow(5, StockCashStubs.Moment, "SALE", q(-3m), m(60.00m), "SALE", 1, "INV-2026-000001", null, q(997m), q(997m), true, false, false),
                new StockCardRow(6, StockCashStubs.Moment.AddHours(1), "ADJUSTMENT", q(-4m), m(59.50m), "ADJUSTMENT", null, string.Empty, "Stock count correction", q(993m), Broken ? q(1000m) : q(993m), !Broken, Sparse, Sparse),
            ],
            q(0m), q(-7m), q(993m), !Broken, !Sparse);
    }
}

internal sealed class StubDamageAdjustmentReportQuery : FaultableStub, IDamageAdjustmentReportQuery
{
    public async Task<DamageAdjustmentReport> GetReportAsync(ReportDateRange range, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        var m = StockCashStubs.M;

        return new DamageAdjustmentReport(
            range,
            [
                new DamageAdjustmentRow(DamageSource.Damage, "Water damage", 2, Quantity.FromDecimal(-8m, 0), m(-656.00m)),
                new DamageAdjustmentRow(DamageSource.DamagedReturn, "Cracked housing", 1, Quantity.FromDecimal(-1m, 0), m(-150.00m)),
                new DamageAdjustmentRow(DamageSource.Adjustment, "Found in back store", 1, Quantity.FromDecimal(3m, 0), m(448.50m)),
            ],
            m(-357.50m), m(806.00m), m(448.50m));
    }
}

internal sealed class StubSupplierPurchaseReportQuery : FaultableStub, ISupplierPurchaseReportQuery
{
    internal long? LastSupplier { get; private set; }

    public async Task<SupplierPurchaseReport> GetReportAsync(ReportDateRange range, long? supplierId = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        LastSupplier = supplierId;
        var m = StockCashStubs.M;

        return new SupplierPurchaseReport(
            range,
            supplierId,
            [new SupplierPurchaseRow(1, "Acme Fasteners", 2, m(9707.50m), m(644.85m), m(10352.35m))],
            [new SupplierPurchaseItemRow(2, "RPT-BOLT-A", "Bolt", Quantity.FromDecimal(150m, 0), m(8711.00m), m(58.07m), m(54.52m), m(65.18m), m(10.66m), 0.1955m)],
            [new CostMovementPoint(2, "RPT-BOLT-A", StockCashStubs.Moment, "Acme Fasteners", "GRN-2026-000001", m(54.52m))],
            m(9707.50m), m(644.85m), m(10352.35m));
    }
}

internal sealed class StubFastMovingReportQuery : FaultableStub, IFastMovingReportQuery
{
    internal int LastTopN { get; private set; }

    public async Task<FastMovingReport> GetReportAsync(ReportDateRange range, int topN = 20, CancellationToken cancellationToken = default)
    {
        await EnterAsync(range);
        LastTopN = topN;
        var m = StockCashStubs.M;

        return new FastMovingReport(
            range,
            topN,
            [new FastMovingRow(1, 4, "RPT-NAIL-A", "Nail", Quantity.FromDecimal(29m, 0), "pc", m(250.00m))],
            [new FastMovingRow(1, 3, "RPT-DRILL-A", "Drill", Quantity.FromDecimal(2m, 0), "pc", m(487.50m))]);
    }
}

internal sealed class StubStockValuationQuery : FaultableStub, IStockValuationQuery
{
    internal StockValuationFilter? LastFilter { get; private set; }

    public Task<StockValuationReport> GetValuationAsync(CancellationToken cancellationToken = default) =>
        GetValuationAsync(new StockValuationFilter(), cancellationToken);

    public async Task<StockValuationReport> GetValuationAsync(StockValuationFilter filter, CancellationToken cancellationToken = default)
    {
        await EnterAsync();
        LastFilter = filter;
        var m = StockCashStubs.M;

        return new StockValuationReport(
            [new StockValuationLine(3, "Drill", "RPT-DRILL-A", "pc", Quantity.FromDecimal(1000m, 0), m(149.50m), m(149500.00m), "Tools", m(250.00m), m(250000.00m))],
            m(149500.00m),
            m(250000.00m));
    }
}

internal sealed class StubSlowMovingStockQuery : FaultableStub, ISlowMovingStockQuery
{
    internal SlowMovingFilter? LastFilter { get; private set; }

    public Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) =>
        FindAsync(new SlowMovingFilter(olderThan), cancellationToken);

    public async Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(SlowMovingFilter filter, CancellationToken cancellationToken = default)
    {
        await EnterAsync();
        LastFilter = filter;
        var m = StockCashStubs.M;
        var q = StockCashStubs.Q;
        var idle = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        IReadOnlyList<SlowMovingStockLine> lines =
        [
            new SlowMovingStockLine(3, "Drill", "RPT-DRILL-A", "pc", q(1000m), idle.AddDays(30), idle, idle, "Tools", m(149.50m), m(149500.00m)),
            new SlowMovingStockLine(6, "Gasket", "EXT-GASKET-A", "pc", q(20m), idle.AddDays(60), null, idle.AddDays(-10), string.Empty, m(9.25m), m(185.00m)),
        ];

        return lines;
    }
}

internal sealed class StubStockOnHandQuery : FaultableStub, IStockOnHandQuery
{
    internal int LineCount { get; set; } = 2;

    internal StockOnHandFilter? LastFilter { get; private set; }

    public async Task<StockOnHandReport> GetStockOnHandAsync(StockOnHandFilter filter, CancellationToken cancellationToken = default)
    {
        await EnterAsync();
        LastFilter = filter;

        var lines = Enumerable.Range(1, LineCount).Select(i => new StockOnHandLine(
            i,
            "SKU-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            i == 1 ? "Nail" : "Item " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Hardware",
            "Acme",
            "A1",
            StockCashStubs.Q(971m),
            "pc",
            i == 1 ? [new StockOnHandAlternateUnit("box", 971m / 12m)] : [],
            StockCashStubs.Q(i == 1 ? 50m : 0m)));

        return new StockOnHandReport([.. lines]);
    }
}

internal sealed class StubReorderListQuery : FaultableStub, IReorderListQuery
{
    internal ReorderListFilter? LastFilter { get; private set; }

    public Task<IReadOnlyList<ReorderListLine>> GetReorderListAsync(CancellationToken cancellationToken = default) =>
        GetReorderListAsync(new ReorderListFilter(), cancellationToken);

    public async Task<IReadOnlyList<ReorderListLine>> GetReorderListAsync(ReorderListFilter filter, CancellationToken cancellationToken = default)
    {
        await EnterAsync();
        LastFilter = filter;
        return [.. Lines()];
    }

    public async Task<IReadOnlyList<ReorderSupplierGroup>> GetReorderListBySupplierAsync(ReorderListFilter filter, CancellationToken cancellationToken = default)
    {
        await EnterAsync();
        LastFilter = filter;
        var lines = Lines();

        return
        [
            new ReorderSupplierGroup(1, "Acme Fasteners", [lines[0]]),
            new ReorderSupplierGroup(null, string.Empty, [lines[1]]),
        ];
    }

    private static List<ReorderListLine> Lines()
    {
        var q = StockCashStubs.Q;

        return
        [
            new ReorderListLine(1, "RPT-BOLT", "Bolt", q(1136m), q(1200m), q(500m), "pc", 1, "Acme Fasteners", "Fasteners"),
            new ReorderListLine(2, "EXT-HINGE", "Hinge", q(10m), q(45m), q(100m), "pc", null, null, "Tools"),
        ];
    }
}

internal sealed class StubReportFilterLookup : IReportFilterLookup
{
    internal int CategoryCalls { get; private set; }

    internal int BrandCalls { get; private set; }

    internal int SupplierCalls { get; private set; }

    internal Exception? Fault { get; set; }

    public Task<IReadOnlyList<ReportFilterOption>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        CategoryCalls++;
        return Answer([new ReportFilterOption(10, "Fasteners"), new ReportFilterOption(11, "Fasteners / Machine screws"), new ReportFilterOption(12, "Tools")]);
    }

    public Task<IReadOnlyList<ReportFilterOption>> ListBrandsAsync(CancellationToken cancellationToken = default)
    {
        BrandCalls++;
        return Answer([new ReportFilterOption(20, "Bosch"), new ReportFilterOption(21, "Makita")]);
    }

    public Task<IReadOnlyList<ReportFilterOption>> ListSuppliersAsync(CancellationToken cancellationToken = default)
    {
        SupplierCalls++;
        return Answer([new ReportFilterOption(1, "Acme Fasteners"), new ReportFilterOption(2, "Zenith Tools"), new ReportFilterOption(3, "Acme Fasteners")]);
    }

    private Task<IReadOnlyList<ReportFilterOption>> Answer(IReadOnlyList<ReportFilterOption> options) =>
        Fault is { } fault ? Task.FromException<IReadOnlyList<ReportFilterOption>>(fault) : Task.FromResult(options);
}

/// <summary>An <see cref="ILogger{T}"/> that remembers every entry, so a test can assert what was logged and at what level.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    internal List<CapturedLog> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add(new CapturedLog(logLevel, eventId, exception, formatter(state, exception)));
    }
}

/// <summary>One line a <see cref="CapturingLogger{T}"/> was given.</summary>
internal sealed record CapturedLog(LogLevel Level, EventId EventId, Exception? Exception, string Message);

/// <summary>A database failure: <see cref="System.Data.Common.DbException"/> is what every SQLite error derives from.</summary>
internal sealed class FakeDbException(string message) : System.Data.Common.DbException(message)
{
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Task P2-T11's own "Done when": all three interim stock reports run in under 10 seconds on a
/// seeded database.
/// </summary>
/// <remarks>
/// This proves the budget against a purpose-built, moderately sized synthetic catalogue - not
/// the 20 000-SKU/100 000-line performance dataset <c>PerformanceDatasetSeeder</c> builds for the
/// Phase 1 regression guard and the on-terminal NFR-P1..P7 gate (HW-T07). Task P2-T11 is
/// explicitly the "operational minimum" ahead of the full Phase 3 report suite
/// (docs/04_PHASE_2_returns_inventory.md), and its own budget is stated against "the seeded
/// database", not that dataset - this is the honest, provable-on-Linux-CI version of the check.
/// </remarks>
public sealed class StockReportPerformanceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AllThreeInterimStockReportsRunUnderTenSecondsOnASeededDatabase()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedSyntheticCatalogueAsync(fixture, productCount: 500);

        var reorderList = fixture.Resolve<IReorderListQuery>();
        var valuation = fixture.Resolve<IStockValuationQuery>();
        var slowMoving = fixture.Resolve<ISlowMovingStockQuery>();

        var reorderStopwatch = Stopwatch.StartNew();
        var reorderResult = await reorderList.GetReorderListAsync();
        reorderStopwatch.Stop();

        var valuationStopwatch = Stopwatch.StartNew();
        var valuationResult = await valuation.GetValuationAsync();
        valuationStopwatch.Stop();

        var slowMovingStopwatch = Stopwatch.StartNew();
        var slowMovingResult = await slowMoving.FindAsync(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        slowMovingStopwatch.Stop();

        // Sanity: the budget is meaningless if the queries silently returned nothing.
        reorderResult.Should().HaveCountGreaterThan(0);
        valuationResult.Lines.Should().HaveCountGreaterThan(0);
        slowMovingResult.Should().HaveCountGreaterThan(0);

        reorderStopwatch.Elapsed.Should().BeLessThan(Budget, "the reorder list is task P2-T11's own 10-second budget");
        valuationStopwatch.Elapsed.Should().BeLessThan(Budget, "the stock valuation report is task P2-T11's own 10-second budget");
        slowMovingStopwatch.Elapsed.Should().BeLessThan(Budget, "the slow-moving report is task P2-T11's own 10-second budget");
    }

    /// <summary>
    /// Inserts <paramref name="productCount"/> products directly by SQL, one commit for the whole
    /// batch - fast, deliberate test setup, not a code path this task's own invariants govern
    /// (CLAUDE.md invariant 3 is about the stock ledger; this writes <c>stock_balance</c> and
    /// <c>stock_movement</c> in lock-step by hand for the same reason
    /// <c>PerformanceDatasetSeeder</c> does for the much larger Phase 1 dataset - going through
    /// <c>IStockLedger.PostAsync</c> and the catalogue maintenance API one row at a time would
    /// make the setup itself slower than the budget being proved).
    /// </summary>
    private static async Task SeedSyntheticCatalogueAsync(SaleFixture fixture, int productCount)
    {
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var supplierId = await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand("Perf Test Supplier", null, null, null, null, null));

        var reorderLevel = Quantity.FromDecimal(20m, pieceUomId).ToScaled();
        var reorderQty = Quantity.FromDecimal(50m, pieceUomId).ToScaled();
        var lowQty = Quantity.FromDecimal(5m, pieceUomId).ToScaled();
        var highQty = Quantity.FromDecimal(500m, pieceUomId).ToScaled();
        var costAvg = Money.FromDecimal(2.00m).ToScaled();

        const string createdAt = "2026-01-01T00:00:00.000+05:30";
        const string oldMovement = "2020-01-01T00:00:00.000+05:30";
        const string recentMovement = "2026-09-01T00:00:00.000+05:30";

        var sql = new StringBuilder();
        sql.Append("BEGIN;\n");

        for (var i = 0; i < productCount; i++)
        {
            var code = "PERF-" + i.ToString("D6", CultureInfo.InvariantCulture);
            var sku = code + "-A";
            var lowStock = i % 2 == 0;
            var qty = lowStock ? lowQty : highQty;
            var occurredAt = lowStock ? oldMovement : recentMovement;

            sql.Append(FormattableString.Invariant($"""
                INSERT INTO product (code, name, base_uom_id, type, tax_class_id, reorder_level, reorder_qty, active, created_at, updated_at)
                VALUES ('{code}', 'Perf {code}', {pieceUomId}, 'STANDARD', {taxClassId}, {reorderLevel}, {reorderQty}, 1, '{createdAt}', '{createdAt}');
                INSERT INTO product_variant (product_id, sku, price, active, created_at)
                VALUES ((SELECT id FROM product WHERE code = '{code}'), '{sku}', 10000, 1, '{createdAt}');
                INSERT INTO stock_balance (product_variant_id, qty_base, cost_avg, updated_at)
                VALUES ((SELECT id FROM product_variant WHERE sku = '{sku}'), {qty}, {costAvg}, '{occurredAt}');
                INSERT INTO stock_movement (product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at)
                VALUES ((SELECT id FROM product_variant WHERE sku = '{sku}'), 'OPENING', {qty}, {costAvg}, 'OPENING', NULL, {qty}, {userId}, '{occurredAt}');
                INSERT INTO product_supplier (product_id, supplier_id, last_cost)
                VALUES ((SELECT id FROM product WHERE code = '{code}'), {supplierId}, {costAvg});

                """));
        }

        sql.Append("COMMIT;\n");

        await fixture.ExecuteAsync(sql.ToString());
    }

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture)
    {
        var uoms = await fixture.Resolve<IUomMaintenance>().ListAsync();
        return uoms.Single(u => u.Name == "Piece").Id;
    }

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture)
    {
        var classes = await fixture.Resolve<ITaxClassMaintenance>().ListAsync();
        return classes.Single(t => t.Name == "Exempt").Id;
    }
}

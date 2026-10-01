using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Inventory;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-08 stock on hand and RPT-10 low stock / reorder (task P3-T06 "Do this" #1), both open to both roles and both
/// cost-free, against the balances at the end of <see cref="StockCashDataset"/>:
/// </summary>
/// <remarks>
/// <code>
/// On hand (by product name):  Bolt 1136 (A1, Fasteners, Bosch, level 1200)   Cord 12.3456 (C1, a Fractional item)
///   Drill 1000 (A1, Tools, Makita, level 1090)   Galvanised bolt M8 100 (A3)   Gasket 20 (B2, level 50)
///   Hinge 10 (C3, Tools, level 45)   Nail 971 (A1; 80.9166... boxes of 12)   Rivet 0 (D4, never stocked)
///   Washer 200 (B2, Machine screws, level 300).  Labour is a Service: not stock-tracked, not listed.
/// Reorder (shortfall = level - on hand): Washer 100 (Zenith), Drill 90 (Acme), Bolt 64 (Acme), Hinge 35 (no supplier),
///   Gasket 30 (Zenith).  Nail, Cord, SKEL, Rivet have level 0 - "not tracked" - and are never proposed.
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class StockOnHandAndReorderReportTests(StockCashReportFixture fixture)
{
    private static readonly string[] SqlConstants = ["LinesSql", "AlternateUnitsSql"];

    private static readonly string[] CostWords = ["cost", "cogs", "profit", "margin", "value", "price"];

    private IStockOnHandQuery OnHand => fixture.Host.Resolve<IStockOnHandQuery>();

    private IReorderListQuery Reorder => fixture.Host.Resolve<IReorderListQuery>();

    // ---- RPT-08 stock on hand ------------------------------------------------------------------

    [Fact]
    public async Task RPT_08_StockOnHandListsEveryStockTrackedItemWithItsQuantityLocationAndReorderLevel()
    {
        var report = await OnHand.GetStockOnHandAsync(new StockOnHandFilter());

        report.Lines.Select(line => (line.Sku, line.Description, line.CategoryName, line.BrandName, line.Location, line.QtyOnHandBase.Value, line.BaseUomSymbol, line.ReorderLevel.Value))
            .Should().Equal(
            [
                ("RPT-BOLT-A", "Bolt", "Fasteners", "Bosch", "A1", 1136m, "pc", 1200m),
                ("EXT-CORD-A", "Cord", string.Empty, string.Empty, "C1", 12.3456m, "pc", 0m),
                ("RPT-DRILL-A", "Drill", "Tools", "Makita", "A1", 1000m, "pc", 1090m),
                ("SKEL-001-A", "Galvanised bolt M8", string.Empty, string.Empty, "A3", 100m, "pc", 0m),
                ("EXT-GASKET-A", "Gasket", string.Empty, string.Empty, "B2", 20m, "pc", 50m),
                ("EXT-HINGE-A", "Hinge", "Tools", string.Empty, "C3", 10m, "pc", 45m),
                ("RPT-NAIL-A", "Nail", string.Empty, string.Empty, "A1", 971m, "pc", 0m),
                ("EXT-RIVET-A", "Rivet", string.Empty, string.Empty, "D4", 0m, "pc", 0m),
                ("EXT-WASHER-A", "Washer", "Machine screws", string.Empty, "B2", 200m, "pc", 300m),
            ], "by product name; the Labour service is not stock-tracked and the never-stocked Rivet shows zero");

        report.Lines.Should().NotContain(line => line.ProductVariantId == fixture.Data.LabourVariantId);
    }

    [Fact]
    public async Task RPT_08_AnItemSoldInAnAlternateUnitShowsItsStockInThatUnitUnrounded()
    {
        var report = await OnHand.GetStockOnHandAsync(new StockOnHandFilter());

        var nail = report.Lines.Single(line => line.Sku == "RPT-NAIL-A");
        nail.AlternateUnits.Should().ContainSingle();
        nail.AlternateUnits[0].Symbol.Should().Be("box");
        nail.AlternateUnits[0].Quantity.Should().Be(971m / 12m, "971 pieces over 12 per box, not rounded to 80.92");

        report.Lines.Where(line => line.Sku != "RPT-NAIL-A").Should().OnlyContain(line => line.AlternateUnits.Count == 0);
    }

    [Fact]
    public async Task RPT_08_TheFiltersNarrowByCategoryBrandSupplierLocationAndInStockOnly()
    {
        var data = fixture.Data;

        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(CategoryId: data.Sales.FastenersCategoryId)))
            .Lines.Select(line => line.Sku).Should().Equal("RPT-BOLT-A", "EXT-WASHER-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(CategoryId: data.MachineScrewsCategoryId)))
            .Lines.Select(line => line.Sku).Should().Equal("EXT-WASHER-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(BrandId: data.Sales.BoschBrandId)))
            .Lines.Select(line => line.Sku).Should().Equal("RPT-BOLT-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(BrandId: data.Sales.MakitaBrandId)))
            .Lines.Select(line => line.Sku).Should().Equal("RPT-DRILL-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(SupplierId: data.ZenithId)))
            .Lines.Select(line => line.Sku).Should().Equal("EXT-GASKET-A", "EXT-WASHER-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(SupplierId: data.AcmeId)))
            .Lines.Select(line => line.Sku).Should().Equal("RPT-BOLT-A", "RPT-DRILL-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(Location: "b2")))
            .Lines.Select(line => line.Sku).Should().Equal(["EXT-GASKET-A", "EXT-WASHER-A"], "a case-insensitive substring of the location");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(Location: "  C ")))
            .Lines.Select(line => line.Sku).Should().Equal(["EXT-CORD-A", "EXT-HINGE-A"], "trimmed, and matching C1 and C3");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(Location: "   ")))
            .Lines.Should().HaveCount(9, "a blank location is no filter");

        var inStock = await OnHand.GetStockOnHandAsync(new StockOnHandFilter(InStockOnly: true));
        inStock.Lines.Should().HaveCount(8);
        inStock.Lines.Should().NotContain(line => line.Sku == "EXT-RIVET-A", "nothing on hand");

        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(CategoryId: data.Sales.ToolsCategoryId, SupplierId: data.AcmeId)))
            .Lines.Select(line => line.Sku).Should().Equal("RPT-DRILL-A");
        (await OnHand.GetStockOnHandAsync(new StockOnHandFilter(Location: "zzz"))).Lines.Should().BeEmpty();
    }

    [Fact]
    public void RPT_08_NoStockOnHandDtoHasACostMarginProfitValueOrPriceProperty()
    {
        var reachable = ReachableTypes(typeof(IStockOnHandQuery));

        reachable.Select(type => type.Name).Should().Contain(["StockOnHandReport", "StockOnHandLine", "StockOnHandAlternateUnit", "StockOnHandFilter"]);

        var offenders = reachable
            .SelectMany(type => type.GetProperties().Select(property => type.Name + "." + property.Name))
            .Where(name => CostWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty("stock on hand is open to a cashier: its DTOs have no cost field at all (CLAUDE.md invariant 8, AC-17)");

        // The control: the same walk finds a cost field on the owner-only valuation's DTOs.
        ReachableTypes(typeof(IStockValuationQuery))
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .Should().Contain(["CostAvg", "Value"]);
    }

    [Fact]
    public void RPT_08_TheStockOnHandSqlNamesNoCostColumnAtAll()
    {
        var sql = string.Join(
            "\n",
            SqlConstants.Select(name =>
                (string)typeof(StockOnHandQuery).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!));

        sql.Should().Contain("stock_balance", "the precondition: this is the query that reads stock");
        sql.Should().NotContainEquivalentOf("cost").And.NotContainEquivalentOf("cogs").And.NotContainEquivalentOf("margin")
            .And.NotContainEquivalentOf("cost_avg").And.NotContainEquivalentOf("price");
        sql.Should().NotContainEquivalentOf("stock_movement", "the projection is read, never the ledger (CLAUDE.md invariant 3)");
    }

    [Fact]
    public void RPT_08_StockOnHandAndTheReorderListAreOpenToBothRolesSoCarryNoRequiredRole()
    {
        typeof(IStockOnHandQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(IReorderListQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(IStockOnHandQuery).GetMethods().Should().OnlyContain(method => method.GetCustomAttribute<RequiresRoleAttribute>() == null);
        typeof(IReorderListQuery).GetMethods().Should().OnlyContain(method => method.GetCustomAttribute<RequiresRoleAttribute>() == null);

        // And the reorder DTOs have no cost either.
        ReachableTypes(typeof(IReorderListQuery))
            .SelectMany(type => type.GetProperties().Select(property => type.Name + "." + property.Name))
            .Where(name => CostWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Should().BeEmpty();
    }

    // ---- RPT-10 low stock and reorder ----------------------------------------------------------

    [Fact]
    public async Task RPT_10_TheReorderListMatchesTheHandWorkedShortfallsFurthestUnderLevelFirst()
    {
        var list = await Reorder.GetReorderListAsync();

        list.Select(line => (line.ProductCode, line.QtyOnHandBase.Value, line.ReorderLevel.Value, line.SuggestedQty.Value, line.PreferredSupplierName, line.CategoryName)).Should().Equal(
        [
            ("EXT-WASHER", 200m, 300m, 400m, "Zenith Tools", "Machine screws"),
            ("RPT-DRILL", 1000m, 1090m, 200m, "Acme Fasteners", "Tools"),
            ("RPT-BOLT", 1136m, 1200m, 500m, "Acme Fasteners", "Fasteners"),
            ("EXT-HINGE", 10m, 45m, 100m, null, "Tools"),
            ("EXT-GASKET", 20m, 50m, 100m, "Zenith Tools", string.Empty),
        ]);

        list.Select(line => line.PreferredSupplierId).Should().Equal(
            fixture.Data.ZenithId, fixture.Data.AcmeId, fixture.Data.AcmeId, null, fixture.Data.ZenithId);
        list.Should().NotContain(line => line.ProductCode == "RPT-NAIL", "a reorder level of zero means not tracked");

        var dashboard = await fixture.Host.Resolve<IDashboardQueries>().GetSummaryAsync();
        dashboard.LowStockCount.Should().Be(list.Count, "the dashboard's count and this list use one predicate");
    }

    [Fact]
    public async Task RPT_10_TheSupplierAndCategoryFiltersNarrowTheList()
    {
        var data = fixture.Data;

        (await Reorder.GetReorderListAsync(new ReorderListFilter(SupplierId: data.ZenithId)))
            .Select(line => line.ProductCode).Should().Equal("EXT-WASHER", "EXT-GASKET");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(SupplierId: data.AcmeId)))
            .Select(line => line.ProductCode).Should().Equal("RPT-DRILL", "RPT-BOLT");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(CategoryId: data.Sales.FastenersCategoryId)))
            .Select(line => line.ProductCode).Should().Equal(["EXT-WASHER", "RPT-BOLT"], "Fasteners and its child Machine screws");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(CategoryId: data.MachineScrewsCategoryId)))
            .Select(line => line.ProductCode).Should().Equal("EXT-WASHER");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(CategoryId: data.Sales.ToolsCategoryId)))
            .Select(line => line.ProductCode).Should().Equal("RPT-DRILL", "EXT-HINGE");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(data.AcmeId, data.Sales.FastenersCategoryId)))
            .Select(line => line.ProductCode).Should().Equal("RPT-BOLT");
        (await Reorder.GetReorderListAsync(new ReorderListFilter(SupplierId: 987654321))).Should().BeEmpty();
        (await Reorder.GetReorderListAsync(new ReorderListFilter())).Should().HaveCount(5);
    }

    [Fact]
    public async Task RPT_10_TheListGroupedBySupplierPutsNoSupplierLinkedLastAndKeepsTheShortfallOrderInsideAGroup()
    {
        var groups = await Reorder.GetReorderListBySupplierAsync(new ReorderListFilter());

        groups.Select(group => (group.SupplierId, group.SupplierName, Codes: string.Join(",", group.Lines.Select(line => line.ProductCode)))).Should().Equal(
        [
            ((long?)fixture.Data.AcmeId, "Acme Fasteners", "RPT-DRILL,RPT-BOLT"),
            ((long?)fixture.Data.ZenithId, "Zenith Tools", "EXT-WASHER,EXT-GASKET"),
            ((long?)null, string.Empty, "EXT-HINGE"),
        ]);

        var fasteners = await Reorder.GetReorderListBySupplierAsync(new ReorderListFilter(CategoryId: fixture.Data.Sales.FastenersCategoryId));
        fasteners.Select(group => group.SupplierName).Should().Equal("Acme Fasteners", "Zenith Tools");
        fasteners.SelectMany(group => group.Lines).Select(line => line.ProductCode).Should().Equal("RPT-BOLT", "EXT-WASHER");

        (await Reorder.GetReorderListBySupplierAsync(new ReorderListFilter(SupplierId: fixture.Data.ZenithId)))
            .Should().ContainSingle().Which.Lines.Should().HaveCount(2);
        (await Reorder.GetReorderListBySupplierAsync(new ReorderListFilter(SupplierId: 987654321))).Should().BeEmpty();
    }

    private static List<Type> ReachableTypes(Type queryInterface)
    {
        var found = new HashSet<Type>();

        foreach (var method in queryInterface.GetMethods())
        {
            Walk(method.ReturnType, queryInterface.Namespace!, found);

            foreach (var parameter in method.GetParameters())
            {
                Walk(parameter.ParameterType, queryInterface.Namespace!, found);
            }
        }

        return [.. found];
    }

    private static void Walk(Type type, string ns, HashSet<Type> found)
    {
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, ns, found);
            }

            return;
        }

        if (type.Namespace != ns || !type.IsClass || !found.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties())
        {
            Walk(property.PropertyType, ns, found);
        }
    }
}

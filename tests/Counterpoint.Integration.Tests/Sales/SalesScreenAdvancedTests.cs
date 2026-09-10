using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// P1-T09's own additions over the walking skeleton's sales screen: the repeat-scan setting,
/// switching a line's unit, open items, line discounts and their cap, held bills, and the
/// negative-stock policy (SRS FR-3.2, FR-2.5, FR-3.7, FR-2.8, FR-3.16-FR-3.18, FR-3.32-FR-3.33,
/// FR-3.13-FR-3.14).
/// </summary>
public sealed class SalesScreenAdvancedTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 9, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_3_2_ScanningTheSameCodeTwiceIncrementsQuantityAndTheSettingFlipsItToTwoLines()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = BuildScreen(fixture);

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Lines.Should().ContainSingle("the default policy combines a repeat scan into the existing line");
        screen.Lines[0].QuantityText.Should().Be("2");

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { CombineRepeatScans = false } });

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Lines.Should().HaveCount(2, "the setting now keeps every scan on its own line");
    }

    [Fact]
    public async Task FR_2_5_SwitchingTheSellingUnitOnALineRepricesItThroughTheConversionFactor()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (productId, variantId, pieceId, boxId, barcode) = await SeedBoxedProductAsync(fixture);
        await SeedOpeningStockAsync(fixture, variantId, pieceId, 1000m);

        var screen = BuildScreen(fixture);
        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);

        var line = screen.Lines.Single();
        line.UnitChoices.Should().Contain(["pc", "box"]);
        line.SelectedUnitSymbol = "box";

        // Community Toolkit's generated setter raises the change synchronously, but the refresh
        // it kicks off is asynchronous fire-and-forget - give it a moment the way a UI does not
        // have to (nothing here is timing-sensitive business logic, only test synchronisation).
        await Task.Delay(50);

        screen.Lines.Single().UnitPriceText.Should().Be("10.00", "1 box = 100 pieces at 0.10 each");
        screen.Lines.Single().QuantityText.Should().Be("1");

        _ = productId;
        _ = boxId;
    }

    [Fact]
    public async Task FR_2_8_AnOpenItemLineIsAddedAndPricedAndFlaggedAsAnOpenItem()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = BuildScreen(fixture);

        await screen.OpenItemCommand.ExecuteAsync(null);
        screen.IsOpenItemPanelOpen.Should().BeTrue();

        screen.OpenItemDescription = "Cut to size";
        screen.OpenItemQuantityText = "2";
        screen.OpenItemPriceText = "150.00";
        screen.OpenItemSelectedUnit = screen.OpenItemUnitChoices.First();

        await screen.AddOpenItemCommand.ExecuteAsync(null);

        screen.IsOpenItemPanelOpen.Should().BeFalse();
        var line = screen.Lines.Should().ContainSingle().Subject;
        line.IsOpenItem.Should().BeTrue();
        line.Description.Should().Be("Cut to size");
        line.LineTotalText.Should().Be("300.00");
        screen.Total.Should().Be("300.00");
    }

    [Fact]
    public async Task FR_3_16_ALineDiscountWithinTheCapAppliesAndAboveTheCapIsRefusedWithAPlainMessage()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { MaxLineDiscountRate = Percentage.FromPercent(10m) } });

        var screen = BuildScreen(fixture);
        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.SelectedLine = screen.Lines.Single();
        screen.DiscountCommand.Execute(null);
        screen.DiscountIsPercent = true;
        screen.DiscountValueText = "10";
        await screen.ApplyDiscountCommand.ExecuteAsync(null);

        screen.Lines.Single().DiscountText.Should().NotBeEmpty("a 10% discount is exactly at the cap");
        screen.Status.Should().NotContain("above");

        screen.SelectedLine = screen.Lines.Single();
        screen.DiscountCommand.Execute(null);
        screen.DiscountValueText = "50";
        await screen.ApplyDiscountCommand.ExecuteAsync(null);

        screen.Status.Should().Contain("above the", "a discount above the cap is refused, not silently capped (SRS FR-3.18)");
    }

    [Fact]
    public async Task FR_3_32_HoldAndRecallPreserveLinesQuantitiesDiscountsAndCustomerExactly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerStore>();
        var customerId = await customers.CreateAsync(new NewCustomer(
            "Kamal Perera", "0771234567", null, null, "RETAIL", Money.Zero));

        var screen = BuildScreen(fixture);
        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.SelectedLine = screen.Lines.Single();
        screen.DiscountCommand.Execute(null);
        screen.DiscountIsPercent = false;
        screen.DiscountValueText = "1";
        await screen.ApplyDiscountCommand.ExecuteAsync(null);

        await screen.CustomerAsync();
        screen.PickCustomer(new CustomerSearchResultViewModel(
            (await customers.FindByIdAsync(customerId))!));
        screen.CurrentCustomerText.Should().Be("Kamal Perera");

        screen.HoldCommand.Execute(null); // F5 opens the hold panel synchronously
        screen.HoldLabel = "Kamal - bolt";
        await screen.ConfirmHoldCommand.ExecuteAsync(null);

        screen.Lines.Should().BeEmpty("the bill is parked, not on screen any more");
        screen.CurrentCustomerText.Should().Be("Walk-in (no customer)");

        await screen.RecallAsync();
        var held = screen.HeldBillRows.Should().ContainSingle().Subject;
        held.Label.Should().Be("Kamal - bolt");

        await screen.RecallSelectedAsync(held);

        var recalledLine = screen.Lines.Should().ContainSingle().Subject;
        recalledLine.QuantityText.Should().Be("1");
        recalledLine.DiscountText.Should().NotBeEmpty("the discount survived the hold and recall");
        screen.CurrentCustomerText.Should().Be("Kamal Perera", "the customer survived the hold and recall");
    }

    [Fact]
    public async Task FR_3_13_TheDefaultPolicyAllowsNegativeStockSilentlyWarnShowsItAndBlockRefusesTheLine()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (_, variantId, pieceId, _, barcode) = await SeedBoxedProductAsync(fixture);

        // No opening stock at all: one piece already takes the balance negative. The shop's
        // answer to Q-11 (docs/README.md) is "Allow" - sold anyway, with nothing alarming shown.
        var screen = BuildScreen(fixture);
        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Status.Should().Be("Boxed nails added.", "the default policy is Allow: sold silently, logged (SRS FR-3.14), not warned about here");
        screen.Lines.Should().ContainSingle();

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { NegativeStock = NegativeStockPolicy.Warn } });

        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Status.Should().Contain("below zero", "SRS FR-3.13: the Warn policy shows it on screen before payment");

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { NegativeStock = NegativeStockPolicy.Block } });

        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Status.Should().Contain("blocks", "SRS FR-3.13: the Block policy refuses the line instead");
        screen.Lines.Should().HaveCount(1, "the third scan was refused, not added as a second line");

        _ = pieceId;
    }

    /// <summary>
    /// Audit gap (P1-T09 review): the "Block" policy is checked line-by-line against the
    /// persisted on-hand balance (<c>CompleteSaleHandler.PriceCatalogueLineAsync</c>), never
    /// against the quantity of the same variant already sitting on other lines of the same bill.
    /// Two separate lines for the same variant only exist when <c>CombineRepeatScans</c> is off
    /// (SRS FR-3.2) - each individually within on-hand stock, but their sum is not. If this
    /// passes, Block does not aggregate same-variant lines within one bill and the cashier can
    /// oversell past a policy whose entire purpose is to refuse exactly that (SRS FR-3.13,
    /// Q-11) - a product defect, not a rounding artefact. This test intentionally asserts the
    /// policy's documented promise; if it fails, that promise is not what the code delivers.
    /// </summary>
    [Fact]
    public async Task FR_3_13_BlockAggregatesTheSameVariantAcrossSeparateLinesNotJustOneLineAtATime()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (_, variantId, pieceId, _, barcode) = await SeedBoxedProductAsync(fixture);
        await SeedOpeningStockAsync(fixture, variantId, pieceId, 5m);

        await fixture.Resolve<ISettings>().UpdateAsync(s => s with
        {
            Policy = s.Policy with { CombineRepeatScans = false, NegativeStock = NegativeStockPolicy.Block },
        });

        var screen = BuildScreen(fixture);

        // Two separate lines for the same variant, 3 pieces each - each alone is well within the
        // 5 on hand, so if the check ever aggregates by variant it must be the second line, not
        // the first, that trips it.
        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.Lines.Single().QuantityText = "3";
        screen.Lines.Single().CommitQuantityCommand.Execute(null);
        screen.Lines.Single().QuantityText.Should().Be("3", "3 of 5 on hand is not a negative-stock line");

        screen.Barcode = barcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.Lines.Should().HaveCount(2, "the setting is off, so the repeat scan is its own line");

        var secondLine = screen.Lines[1];
        secondLine.QuantityText = "3";
        secondLine.CommitQuantityCommand.Execute(null);

        // 3 + 3 = 6 pieces against 5 on hand: the shop's Block policy exists precisely to refuse
        // this. Each line alone (3) never exceeds 5, so a check that does not aggregate by
        // variant across the bill's own lines has nothing to trip on here.
        screen.Status.Should().Contain(
            "blocks",
            "SRS FR-3.13/Q-11: Block must refuse an oversell reached by two lines of the same " +
            "variant, not only by one line alone - otherwise splitting a scan across lines with " +
            "CombineRepeatScans off is an unaudited way around the policy");
        screen.Lines[1].QuantityText.Should().Be(
            "1", "the refused edit must be rolled back, exactly like a single-line block is (see the sibling test above)");
    }

    private static SalesViewModel BuildScreen(SaleFixture fixture) => new(
        fixture.Resolve<IScanItem>(),
        fixture.Resolve<IQuoteSale>(),
        fixture.Resolve<ICompleteSale>(),
        fixture.Resolve<ITillSessionProvider>(),
        fixture.Resolve<ISession>(),
        fixture.Resolve<ISettings>(),
        fixture.Resolve<IProductSearchService>(),
        fixture.Resolve<ICustomerStore>(),
        fixture.Resolve<IUomStore>(),
        fixture.Resolve<IStockEnquiry>(),
        fixture.Resolve<IHeldBillService>(),
        fixture.Resolve<IOpenShift>(),
        fixture.Resolve<IDashboardQueries>(),
        fixture.Resolve<IReprintReceipt>(),
        fixture.Resolve<IPrintJobOutbox>(),
        fixture.Resolve<TimeProvider>());

    /// <summary>A standard product with a box unit alongside its base piece unit, and a barcode to scan (SRS FR-2.4, FR-2.5).</summary>
    private static async Task<(long ProductId, long VariantId, long PieceUomId, long BoxUomId, string Barcode)> SeedBoxedProductAsync(SaleFixture fixture)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var barcodes = fixture.Resolve<IBarcodeMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "box", 0));

        var productId = await products.CreateAsync(new SaveProductCommand(
            "NAIL-BOXED",
            "Boxed nails",
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceId,
            ProductType.Standard,
            exemptId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("NAIL-BOXED-A", new Dictionary<string, string> { ["pack"] = "std" }, Money.FromDecimal(0.10m)));

        await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), SellingPrice: null));

        var barcode = "9999999999" + variantId.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
        await barcodes.AddAsync(variantId, barcode, makePrimary: true);

        return (productId, variantId, pieceId, boxId, barcode);
    }

    private static async Task SeedOpeningStockAsync(SaleFixture fixture, long variantId, long uomId, decimal quantity)
    {
        var ledger = fixture.Resolve<IStockLedger>();
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await ledger.PostAsync(
            new StockPosting(
                variantId,
                "OPENING",
                Quantity.FromDecimal(quantity, uomId),
                Money.FromDecimal(0.05m),
                "OPENING",
                RefDocId: null,
                userId,
                SoldAt));
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// P1-T10's own additions to a completed sale: split tender and change, the tender-versus-total
/// check that runs before the transaction opens, the drawer kick as it actually reaches the
/// printed byte stream through the payment panel's own code path, and the COGS snapshot bug fix
/// (SRS FR-3.16-FR-3.22, FR-8.2, AC-02).
/// </summary>
public sealed class CompleteSaleTenderTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_02_ATenLineBillWithADecimalQuantityItemAUnitSwitchADiscountAndSplitTenderCompletesAndPrintsCorrectly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var pieceUomId = await PieceUomIdAsync(fixture);
        var exemptTaxClassId = await ExemptTaxClassIdAsync(fixture);

        // Line 1: the seeded bolt - a whole-unit standard item, and the one line carrying a
        // discount (SRS FR-3.16).
        var boltVariantId = await SeededVariantIdAsync(fixture);

        // Line 2: a decimal-quantity item - wire sold by the metre, to two decimal places
        // (SRS FR-2.1-FR-2.8, ProductType.Fractional).
        var (wireVariantId, meterUomId) = await SeedWireProductAsync(fixture, exemptTaxClassId);

        // Line 3: a unit switch - boxed nails, sold on this bill by the box (100 pieces), not the
        // product's own base piece unit (SRS FR-2.5, FR-3.7).
        var (nailsVariantId, boxUomId) = await SeedBoxedNailsAsync(fixture, exemptTaxClassId);

        // Lines 4-10: seven more standard, whole-unit items, to round the bill out to ten lines.
        var fillerVariantIds = new List<long>();
        for (var i = 0; i < 7; i++)
        {
            fillerVariantIds.Add(await SeedFillerVariantAsync(fixture, pieceUomId, exemptTaxClassId, i));
        }

        var lines = new List<SaleLineRequest>
        {
            new(boltVariantId, 3m, Discount: DiscountInput.OfRate(Percentage.FromPercent(5m))),
            new(wireVariantId, 2.75m, meterUomId),
            new(nailsVariantId, 1m, boxUomId),
        };
        lines.AddRange(fillerVariantIds.Select(id => new SaleLineRequest(id, 1m)));

        lines.Should().HaveCount(10, "AC-02 is specifically about a ten-line bill");

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);
        quote.Lines.Single(l => l.ProductVariantId == boltVariantId).Discount.Should().BeGreaterThan(
            Money.Zero, "the bolt line carries the bill's discount");

        // Split tender: card takes half (rounded down to the storage scale), cash takes exactly
        // what remains - the two are constructed to sum to the total by definition, so this is a
        // test of completion and persistence, not of TenderCalculator's own arithmetic (that is
        // TenderCalculatorTests's job).
        var half = Money.FromScaled(quote.Total.ToScaled() / 2);
        var remainder = quote.Total - half;

        var tenders = new List<TenderRequest>
        {
            new(TenderTypes.Card, half, "SLIP-42"),
            new(TenderTypes.Cash, remainder),
        };

        var completed = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            SoldAt,
            lines,
            tenders));

        completed.Total.Should().Be(quote.Total);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_line WHERE sale_id = " + completed.SaleId + ";"))
            .Should().Be(10);

        var sumPayments = await fixture.ScalarAsync(
            "SELECT SUM(amount) FROM payment WHERE sale_id = " + completed.SaleId + ";");
        sumPayments.Should().Be(
            quote.Total.ToScaled().ToString(CultureInfo.InvariantCulture),
            "sum(payment.amount) must equal sale.total exactly, for every split (SRS FR-3.26)");

        var saleTotal = await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + completed.SaleId + ";");
        saleTotal.Should().Be(quote.Total.ToScaled().ToString(CultureInfo.InvariantCulture));

        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_id = " + completed.SaleId + ";"))
            .Should().Be(1, "the receipt is queued inside the transaction");

        var printed = await fixture.Resolve<PrintWorker>().DrainAsync();
        printed.Should().Be(1);

        Directory.GetFiles(fixture.ReceiptDirectory, "*.bin")
            .Should().ContainSingle("the rendered byte stream reached the outbox and was printed");
    }

    [Fact]
    public async Task FR_3_25_AnOverTenderOnANonCashTypeIsRejectedAndNothingIsWrittenNorIsTheBillNumberConsumed()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var variantId = await SeededVariantIdAsync(fixture);
        var lines = new List<SaleLineRequest> { new(variantId, 1m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var command = new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            SoldAt,
            lines,
            [new TenderRequest(TenderTypes.Card, quote.Total + Money.FromDecimal(5.00m))]);

        var act = async () => await fixture.Resolve<ICompleteSale>().CompleteAsync(command);

        // TenderCalculator.Calculate runs before CompleteSaleHandler opens the transaction
        // (CompleteSaleHandler.CompleteAsync), so a card tender offered for more than the bill
        // owes has to be refused before a single row is written and before the bill number is
        // ever drawn from number_sequence - exactly the same discipline FR_3_30's under-tender
        // sibling test proves for a tender that falls short.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Change is cash only*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_line;")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment;")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'SALE';"))
            .Should().Be(0, "the seeded OPENING count is the only movement that should exist");
        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SALE_COMPLETED';"))
            .Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job;")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("1", "the bill was refused before the number was allocated");
    }

    [Fact]
    public async Task FR_7_7_ACashTenderOpensTheDrawerAndACardTenderDoesNotThroughTheRealCompletionPath()
    {
        // Not EscPosSaleReceiptRendererTests's hand-built SaleReceipt - a real completed sale,
        // through CompleteSaleHandler's own tenderPlan.Applied, out through the print outbox and
        // FileReceiptPrinter, so the payment panel's new code path is what is actually proven.
        await using var cashFixture = await SaleFixture.CreateSignedInAsync();
        await CompleteOneAsync(cashFixture, TenderTypes.Cash);
        await cashFixture.Resolve<PrintWorker>().DrainAsync();

        var cashBytes = await File.ReadAllBytesAsync(
            Directory.GetFiles(cashFixture.ReceiptDirectory, "*.bin").Single());

        FindSequence(cashBytes, DrawerKick).Should().BeGreaterThan(-1, "a cash tender opens the drawer");

        await using var cardFixture = await SaleFixture.CreateSignedInAsync();
        await CompleteOneAsync(cardFixture, TenderTypes.Card);
        await cardFixture.Resolve<PrintWorker>().DrainAsync();

        var cardBytes = await File.ReadAllBytesAsync(
            Directory.GetFiles(cardFixture.ReceiptDirectory, "*.bin").Single());

        FindSequence(cardBytes, DrawerKick).Should().Be(
            -1, "a card sale that popped the drawer would be a reconciliation problem");
    }

    [Fact]
    public async Task P1_T10_ASaleLineSnapshotsUnitCostFromStockBalanceCostAvgNotProductCostAvg()
    {
        // Regression for the bug SqliteProductLookup's remarks describe: unit_cost must come from
        // stock_balance.cost_avg (what the moving-average formula actually maintains), never from
        // product.cost_avg (set once by the product editor and never kept in step afterwards).
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);

        // The seeder leaves the shelf at 100 pieces, cost 9.0000, and product.cost_avg also at
        // 9.0000. A GRN-shaped receipt of 50 more at 15.0000 moves stock_balance.cost_avg to
        // (100*9 + 50*15) / 150 = 11.0000 - but nothing in this system ever touches
        // product.cost_avg again, so the two columns now disagree on purpose.
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "GRN",
            Quantity.FromDecimal(50m, await PieceUomIdAsync(fixture)),
            Money.FromDecimal(15.00m),
            "GRN",
            RefDocId: null,
            userId,
            SoldAt));

        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("110000", "the ledger's own moving-average formula");

        var productCostAvg = await fixture.ScalarAsync(
            "SELECT cost_avg FROM product WHERE id = (SELECT product_id FROM product_variant WHERE id = " + variantId + ");");
        productCostAvg.Should().Be("90000", "product.cost_avg is set once at seed time and never moves");

        var lines = new List<SaleLineRequest> { new(variantId, 5m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var completed = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            await SeededShiftIdAsync(fixture),
            SoldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)]));

        var snapshottedCost = await fixture.ScalarAsync(
            "SELECT unit_cost FROM sale_line WHERE sale_id = " + completed.SaleId + ";");

        snapshottedCost.Should().Be(
            "110000",
            "sale_line.unit_cost must be the ledger's stock_balance.cost_avg at the moment of "
            + "sale, not the stale product.cost_avg column - the exact bug SqliteProductLookup's "
            + "P1-T10 remarks describe fixing");
    }

    /// <summary>ESC p 0 25 250 - the drawer-kick byte sequence.</summary>
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 25, 250];

    private static async Task CompleteOneAsync(SaleFixture fixture, string tenderType)
    {
        var variantId = await SeededVariantIdAsync(fixture);
        var lines = new List<SaleLineRequest> { new(variantId, 1m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            SoldAt,
            lines,
            [new TenderRequest(tenderType, quote.Total)]));
    }

    private static int FindSequence(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var match = true;
            for (var i = 0; i < needle.Length; i++)
            {
                if (haystack[start + i] != needle[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return start;
            }
        }

        return -1;
    }

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

    /// <summary>A decimal-quantity product: wire, sold by the metre to two decimal places.</summary>
    private static async Task<(long VariantId, long MeterUomId)> SeedWireProductAsync(
        SaleFixture fixture, long taxClassId)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var meterId = await uoms.CreateAsync(new SaveUomCommand("Metre", "m", 2));

        var productId = await products.CreateAsync(new SaveProductCommand(
            "WIRE-001",
            "Galvanised wire",
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            meterId,
            ProductType.Fractional,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("WIRE-001-A", new Dictionary<string, string>(), Money.FromDecimal(100.00m)));

        var userId = await SeededUserIdAsync(fixture);
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(500m, meterId),
            Money.FromDecimal(60.00m),
            "OPENING",
            RefDocId: null,
            userId,
            SoldAt));

        return (variantId, meterId);
    }

    /// <summary>A boxed product: nails, whose bill line switches from the base piece unit to a box of 100.</summary>
    private static async Task<(long VariantId, long BoxUomId)> SeedBoxedNailsAsync(
        SaleFixture fixture, long taxClassId)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);

        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box of 100 nails", "box", 0));

        var productId = await products.CreateAsync(new SaveProductCommand(
            "NAIL-BOXED",
            "Boxed nails",
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("NAIL-BOXED-A", new Dictionary<string, string>(), Money.FromDecimal(0.10m)));

        await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), SellingPrice: null));

        var userId = await SeededUserIdAsync(fixture);
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(1000m, pieceUomId),
            Money.FromDecimal(0.05m),
            "OPENING",
            RefDocId: null,
            userId,
            SoldAt));

        return (variantId, boxId);
    }

    /// <summary>A plain whole-unit filler variant, to round a bill out to ten lines.</summary>
    private static async Task<long> SeedFillerVariantAsync(
        SaleFixture fixture, long pieceUomId, long taxClassId, int index)
    {
        var products = fixture.Resolve<IProductMaintenance>();

        var code = "FILL-" + index.ToString("000", CultureInfo.InvariantCulture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Filler item " + index.ToString(CultureInfo.InvariantCulture),
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,

            // FR-2.24's own similar-name warning would otherwise trip on ten filler products
            // named "Filler item N" - a real shop would see the warning and confirm once; the
            // test asks for exactly what a cashier's second click would send.
            ConfirmDuplicate: true));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", new Dictionary<string, string>(), Money.FromDecimal(5.00m + index)));

        var userId = await SeededUserIdAsync(fixture);
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(50m, pieceUomId),
            Money.FromDecimal(2.00m),
            "OPENING",
            RefDocId: null,
            userId,
            SoldAt));

        return variantId;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// The money identities of engineering guide §4.1, over a bill that actually carries tax
/// (SRS FR-3.19, FR-3.20, DM-01).
/// </summary>
/// <remarks>
/// <para>
/// The seeded catalogue is zero rated on purpose - the shop's real rate is Q-02's and arrives
/// with P1-T03 - which means every other test in this project exercises the sale path with
/// <c>tax = 0</c>, where the tax and rounding arithmetic cannot be wrong. This fixture seeds a
/// product at a rate that is neither zero nor round so that it can be.
/// </para>
/// <para>
/// Every assertion here reads the raw <c>sale</c> and <c>sale_line</c> columns rather than the
/// handler's return value. The identities have to hold over the scaled integers on disk: a
/// decimal that reconciles in memory can still be written as a row that does not, because each
/// column is quantised to the storage scale independently on the way out.
/// </para>
/// </remarks>
public sealed class TaxedSaleTests
{
    /// <summary>
    /// 8.25% of 12.34 is 1.01805 - exactly half a unit of the storage scale, so it is the case
    /// where quantising the line and quantising the header can disagree.
    /// </summary>
    private const decimal TaxPercent = 8.25m;

    private const decimal UnitPrice = 12.34m;

    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_3_20_ATaxedBillIsStoredWithTheExactColumnsItsArithmeticProduces()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var variantId = await SeedTaxedVariantAsync(fixture);

        var completed = await CompleteAsync(fixture, variantId, quantity: 1m);

        var header = await fixture.ScalarAsync(
            "SELECT subtotal || '|' || bill_discount || '|' || tax || '|' || rounding || '|' || total "
            + "FROM sale WHERE id = " + completed.SaleId + ";");

        // 12.34 net; 12.34 x 0.0825 = 1.01805, quantised to 1.0181 at the storage scale; the
        // bill total rounds 13.3581 to 13.36, and rounding carries the 0.0019 difference so the
        // stored row adds up to the stored total.
        header.Should().Be("123400|0|10181|19|133600");

        var line = await fixture.ScalarAsync(
            "SELECT line_total || '|' || tax_rate || '|' || tax FROM sale_line WHERE sale_id = "
            + completed.SaleId + ";");

        line.Should().Be("123400|825|10181", "the line carries the tax the header was built from");
    }

    [Fact]
    public async Task FR_3_20_TheStoredBillReconcilesToItsStoredLinesAndTotal()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var variantId = await SeedTaxedVariantAsync(fixture);

        var completed = await CompleteAsync(fixture, variantId, quantity: 1m);

        await AssertStoredIdentitiesAsync(fixture, completed.SaleId);
    }

    [Fact]
    public async Task FR_3_20_TaxRoundingOnSeveralLinesDoesNotAccumulateIntoTheHeader()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var variantId = await SeedTaxedVariantAsync(fixture);

        // Three lines whose tax each lands on the same half-unit boundary. Summed unrounded and
        // quantised once, the header's tax would be 3.0542; summed after quantising each line,
        // it is 3.0543 - which is what the three stored lines actually add up to.
        var completed = await CompleteAsync(fixture, variantId, quantity: 1m, lineCount: 3);

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM sale_line WHERE sale_id = " + completed.SaleId + ";"))
            .Should().Be(3);

        (await fixture.ScalarAsync("SELECT tax FROM sale WHERE id = " + completed.SaleId + ";"))
            .Should().Be("30543");

        await AssertStoredIdentitiesAsync(fixture, completed.SaleId);
    }

    /// <summary>
    /// The three identities engineering guide §4.1 requires before a bill is persisted, checked
    /// after the fact against the rows that were persisted.
    /// </summary>
    private static async Task AssertStoredIdentitiesAsync(SaleFixture fixture, long saleId)
    {
        var id = saleId.ToString(CultureInfo.InvariantCulture);

        (await fixture.ScalarAsync(
            "SELECT (SELECT SUM(line_total) FROM sale_line WHERE sale_id = " + id + ") "
            + "= (SELECT subtotal FROM sale WHERE id = " + id + ");"))
            .Should().Be("1", "sum(line_total) == subtotal, over the rows as stored");

        (await fixture.ScalarAsync(
            "SELECT (SELECT SUM(tax) FROM sale_line WHERE sale_id = " + id + ") "
            + "= (SELECT tax FROM sale WHERE id = " + id + ");"))
            .Should().Be("1", "the bill's tax is the sum of its lines' tax, over the rows as stored");

        (await fixture.ScalarAsync(
            "SELECT subtotal - bill_discount + tax + rounding = total FROM sale WHERE id = " + id + ";"))
            .Should().Be("1", "subtotal - bill_discount + tax + rounding == total, over the row as stored");

        (await fixture.ScalarAsync(
            "SELECT (SELECT SUM(amount) FROM payment WHERE sale_id = " + id + ") "
            + "= (SELECT total FROM sale WHERE id = " + id + ");"))
            .Should().Be("1", "sum(payments) == total, over the rows as stored");
    }

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture,
        long variantId,
        decimal quantity,
        int lineCount = 1)
    {
        var lines = new List<SaleLineRequest>();
        for (var i = 0; i < lineCount; i++)
        {
            lines.Add(new SaleLineRequest(variantId, quantity));
        }

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;"),
                await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;"),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    /// <summary>
    /// Adds a second product to the seeded catalogue, taxed at <see cref="TaxPercent"/> and
    /// priced at <see cref="UnitPrice"/>, with an opening count posted through the ledger.
    /// </summary>
    private static Task<long> SeedTaxedVariantAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass
            {
                Name = "Standard rated",
                Rate = TaxRate.FromPercent(TaxPercent),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "TAXED-001",
                Name = "Brass elbow 15mm",
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(7.00m),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = "B1",
                NonReturnable = false,
                MinSellQty = 0,
                MaxDiscountRate = null,
                WarrantyDays = null,
                Notes = null,
                ImagePath = null,
                Active = true,
                CreatedAt = SoldAt,
                UpdatedAt = SoldAt,
            };

            context.Add(product);
            await context.SaveChangesAsync(token);

            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = "TAXED-001-A",
                Attributes = """{"size":"15mm"}""",
                Price = Money.FromDecimal(UnitPrice),
                Active = true,
                CreatedAt = SoldAt,
            };

            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id,
                    "OPENING",
                    Quantity.FromDecimal(100m, uomId),
                    Money.FromDecimal(7.00m),
                    "OPENING",
                    RefDocId: null,
                    userId,
                    SoldAt),
                token);

            return variant.Id;
        });
    }
}

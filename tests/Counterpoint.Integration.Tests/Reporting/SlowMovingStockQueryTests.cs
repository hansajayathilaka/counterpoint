using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The slow-moving and non-moving stock report (task P2-T11 "Do this" #3), through the real
/// SQLite database <see cref="SaleFixture"/> composes.
/// </summary>
public sealed class SlowMovingStockQueryTests
{
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task FR_4_OnlyStockThatHasNotMovedSinceTheCutoffIsListedOldestFirst()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (staleVariantId, _, pieceUomId) = await SeedProductWithVariantAsync(fixture, "SLOW-STALE");
        var (staleVariantId2, _, _) = await SeedProductWithVariantAsync(fixture, "SLOW-STALER");
        var (freshVariantId, _, _) = await SeedProductWithVariantAsync(fixture, "SLOW-FRESH");

        var staler = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.FromHours(5.5));
        var stale = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.FromHours(5.5));
        var fresh = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(5.5));
        var cutoff = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.FromHours(5.5));

        await PostOpeningStockAsync(fixture, staleVariantId2, pieceUomId, 10m, 1.00m, staler);
        await PostOpeningStockAsync(fixture, staleVariantId, pieceUomId, 10m, 1.00m, stale);
        await PostOpeningStockAsync(fixture, freshVariantId, pieceUomId, 10m, 1.00m, fresh);

        var result = await fixture.Resolve<ISlowMovingStockQuery>().FindAsync(cutoff);

        result.Should().NotContain(line => line.ProductVariantId == freshVariantId);

        var stalerLine = result.Should().Contain(line => line.ProductVariantId == staleVariantId2).Subject;
        var staleLine = result.Should().Contain(line => line.ProductVariantId == staleVariantId).Subject;

        stalerLine.LastMovementAt.Should().Be(staler);
        staleLine.LastMovementAt.Should().Be(stale);
        stalerLine.QtyOnHandBase.Value.Should().Be(10m);

        // Oldest first.
        Array.IndexOf([.. result], stalerLine).Should().BeLessThan(Array.IndexOf([.. result], staleLine));
    }

    [Fact]
    public async Task AVariantWithNoStockOnHandIsNeverListedEvenIfItsOnlyMovementIsVeryOld()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (variantId, _, pieceUomId) = await SeedProductWithVariantAsync(fixture, "SLOW-ZERO");
        var veryOld = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Stocked, then fully sold off - the balance is now zero, so there is nothing left on
        // the shelf for a slow-moving report to flag, however old the last movement is.
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 10m, 1.00m, veryOld);
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "SALE",
            Quantity.FromDecimal(-10m, pieceUomId),
            Money.FromDecimal(1.00m),
            "SALE",
            RefDocId: null,
            userId,
            veryOld.AddMinutes(1)));

        var result = await fixture.Resolve<ISlowMovingStockQuery>()
            .FindAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        result.Should().NotContain(line => line.ProductVariantId == variantId);
    }

    // ---- Shared seeding -----------------------------------------------------------------------

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture)
    {
        var uoms = await fixture.Resolve<IUomMaintenance>().ListAsync();
        foreach (var uom in uoms)
        {
            if (uom.Name == "Piece")
            {
                return uom.Id;
            }
        }

        throw new InvalidOperationException("The seeded catalogue has no 'Piece' unit.");
    }

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture)
    {
        var classes = await fixture.Resolve<ITaxClassMaintenance>().ListAsync();
        foreach (var taxClass in classes)
        {
            if (taxClass.Name == "Exempt")
            {
                return taxClass.Id;
            }
        }

        throw new InvalidOperationException("The seeded catalogue has no 'Exempt' tax class.");
    }

    private static async Task<(long VariantId, long ProductId, long PieceUomId)> SeedProductWithVariantAsync(
        SaleFixture fixture, string code)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Product " + code,
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
            ConfirmDuplicate: true));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(1.00m)));

        return (variantId, productId, pieceUomId);
    }

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost, DateTimeOffset occurredAt)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, uomId),
            Money.FromDecimal(unitCost),
            "OPENING",
            RefDocId: null,
            userId,
            occurredAt));
    }
}

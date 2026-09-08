using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// <see cref="IRebuildStockBalance"/> replays the ledger into the projection
/// (P1-T07, CLAUDE.md invariant 3).
/// </summary>
/// <remarks>
/// <see cref="P1_T07_RebuildReproducesTheProjectionExactlyAfterARandomSequenceOfMovements"/> is a
/// quick sanity check at a scale that runs in a couple of seconds.
/// <see cref="P1_T07_RebuildReproducesTheProjectionExactlyAfter10000RandomMovements"/> is the
/// exhaustive version the task's "Done when" list actually names - 10 000 movements, still through
/// the real <c>IStockLedger</c>, inside one transaction so it stays fast (~9 s end to end on the
/// dev container: one <c>BEGIN IMMEDIATE</c> and one fsync, not ten thousand of each).
/// </remarks>
public sealed class RebuildStockBalanceCommandTests
{
    private const int MovementCount = 400;
    private const int Seed = 20_260_908;

    /// <summary>
    /// The scale the task's own "Done when" names. Measured at ~9 s end to end (fixture creation,
    /// 10 000 posts inside one transaction, rebuild, and the comparison) on the dev container, so
    /// it is left as an ordinary <c>[Fact]</c> rather than something CI has to opt into.
    /// </summary>
    private const int FullScaleMovementCount = 10_000;
    private const int FullScaleSeed = 20_260_909;
    private const int FullScaleVariantCount = 10;

    [Fact]
    public async Task P1_T07_RebuildReproducesTheProjectionExactlyAfterARandomSequenceOfMovements()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var variantIds = await SeedVariantsAsync(fixture, extraCount: 4);

        var before = await PostRandomMovementsAndReadProjectionAsync(
            fixture, variantIds, MovementCount, Seed);

        var summary = await fixture.Resolve<IRebuildStockBalance>().RebuildAsync();
        summary.MovementCount.Should().BeGreaterThanOrEqualTo(MovementCount);

        var after = await ReadProjectionAsync(fixture);

        after.Should().BeEquivalentTo(
            before,
            "replaying the ledger, in the order it was posted, must reproduce the projection exactly");
    }

    /// <summary>
    /// The exhaustive version the "Done when" list actually asks for (P1-T07): 10 000 random
    /// movements across 10 variants, both signs, a spread of costs - and the rebuilt projection
    /// must land on exactly the same scaled-integer quantity and moving-average cost the
    /// incremental posts already produced, for every variant, not just on aggregate.
    /// </summary>
    [Fact]
    public async Task P1_T07_RebuildReproducesTheProjectionExactlyAfter10000RandomMovements()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var variantIds = await SeedVariantsAsync(fixture, extraCount: FullScaleVariantCount - 1);

        var before = await PostRandomMovementsAndReadProjectionAsync(
            fixture, variantIds, FullScaleMovementCount, FullScaleSeed);

        // Every seeded variant actually moved, so the comparison below is over all of them, not
        // just whichever ones the random draw happened to touch.
        before.Should().HaveCount(FullScaleVariantCount);

        var summary = await fixture.Resolve<IRebuildStockBalance>().RebuildAsync();
        summary.MovementCount.Should().BeGreaterThanOrEqualTo(FullScaleMovementCount);

        var after = await ReadProjectionAsync(fixture);

        after.Should().BeEquivalentTo(
            before,
            "replaying 10 000 movements, in the order they were posted, must reproduce the "
            + "projection exactly - the same scaled-integer quantity and cost_avg for every "
            + "variant, to the last unit and the last hundredth of a cent");
    }

    /// <summary>The seeded variant plus <paramref name="extraCount"/> freshly created ones.</summary>
    private static async Task<List<long>> SeedVariantsAsync(SaleFixture fixture, int extraCount)
    {
        var baseUomId = await fixture.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");
        var taxClassId = await fixture.CountAsync("SELECT id FROM tax_class ORDER BY id LIMIT 1;");

        var variantIds = new List<long>
        {
            await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;"),
        };

        var products = fixture.Resolve<IProductMaintenance>();
        for (var i = 0; i < extraCount; i++)
        {
            var productId = await products.CreateAsync(new SaveProductCommand(
                $"RND-{i}", $"Random product {i}", null, null, null, baseUomId,
                ProductType.Standard, taxClassId, null, false, null, null, null));

            variantIds.Add(await products.CreateVariantAsync(
                productId,
                new SaveProductVariantCommand($"RND-{i}-A", new Dictionary<string, string>(), Money.FromDecimal(1m))));
        }

        return variantIds;
    }

    /// <summary>
    /// Posts <paramref name="movementCount"/> random movements, spread across
    /// <paramref name="variantIds"/>, through the real <see cref="IStockLedger"/>, and returns the
    /// projection they left behind - the "before" a rebuild must reproduce exactly.
    /// </summary>
    private static async Task<Dictionary<long, (long QtyBase, long CostAvg)>> PostRandomMovementsAndReadProjectionAsync(
        SaleFixture fixture,
        List<long> variantIds,
        int movementCount,
        int seed)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var baseUomId = await fixture.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");

        var random = new Random(seed);
        var startedAt = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.FromHours(5.5));

        var ledger = fixture.Resolve<IStockLedger>();
        var unitOfWork = fixture.Resolve<IUnitOfWork>();

        // One outer transaction: PostAsync joins it rather than opening one of its own per
        // movement (SqliteUnitOfWork's ambient-transaction re-entrancy), which is what keeps this
        // fast even at 10 000 movements - a single BEGIN IMMEDIATE and a single fsync on commit
        // (CLAUDE.md invariant 9's synchronous=FULL), not ten thousand of each.
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            for (var i = 0; i < movementCount; i++)
            {
                var variantId = variantIds[random.Next(variantIds.Count)];
                var signedQty = random.Next(2) == 0 ? random.Next(1, 60) : -random.Next(1, 60);
                var cost = Money.FromDecimal(random.Next(50, 5000) / 100m);

                await ledger.PostAsync(
                    new StockPosting(
                        variantId,
                        "ADJUSTMENT",
                        Quantity.FromDecimal(signedQty, baseUomId),
                        cost,
                        "ADJUSTMENT",
                        RefDocId: null,
                        userId,
                        startedAt.AddSeconds(i)),
                    token);
            }
        });

        var before = await ReadProjectionAsync(fixture);
        before.Should().NotBeEmpty();
        return before;
    }

    private static async Task<Dictionary<long, (long QtyBase, long CostAvg)>> ReadProjectionAsync(SaleFixture fixture)
    {
        var connection = await fixture.OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT product_variant_id, qty_base, cost_avg FROM stock_balance ORDER BY product_variant_id;";

            await using var reader = await command.ExecuteReaderAsync();

            var result = new Dictionary<long, (long, long)>();
            while (await reader.ReadAsync())
            {
                result[reader.GetInt64(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }

            return result;
        }
    }
}

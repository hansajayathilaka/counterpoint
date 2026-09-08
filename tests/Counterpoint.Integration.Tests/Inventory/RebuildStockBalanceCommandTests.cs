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
/// A sanity check at a scale this test can run in seconds, over several variants and both signs
/// of movement. The exhaustive 10 000-random-movement version this task's "Done when" describes
/// is the dedicated test-engineer pass's, not this one's.
/// </remarks>
public sealed class RebuildStockBalanceCommandTests
{
    private const int MovementCount = 400;
    private const int Seed = 20_260_908;

    [Fact]
    public async Task P1_T07_RebuildReproducesTheProjectionExactlyAfterARandomSequenceOfMovements()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var baseUomId = await fixture.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");
        var taxClassId = await fixture.CountAsync("SELECT id FROM tax_class ORDER BY id LIMIT 1;");

        var variantIds = new List<long>
        {
            await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;"),
        };

        var products = fixture.Resolve<IProductMaintenance>();
        for (var i = 0; i < 4; i++)
        {
            var productId = await products.CreateAsync(new SaveProductCommand(
                $"RND-{i}", $"Random product {i}", null, null, null, baseUomId,
                ProductType.Standard, taxClassId, null, false, null, null, null));

            variantIds.Add(await products.CreateVariantAsync(
                productId,
                new SaveProductVariantCommand($"RND-{i}-A", new Dictionary<string, string>(), Money.FromDecimal(1m))));
        }

        var random = new Random(Seed);
        var startedAt = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.FromHours(5.5));

        var ledger = fixture.Resolve<IStockLedger>();
        var unitOfWork = fixture.Resolve<IUnitOfWork>();

        // One outer transaction: PostAsync joins it rather than opening 400 of its own
        // (SqliteUnitOfWork's ambient-transaction re-entrancy), which is what keeps this fast.
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            for (var i = 0; i < MovementCount; i++)
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

        var summary = await fixture.Resolve<IRebuildStockBalance>().RebuildAsync();
        summary.MovementCount.Should().BeGreaterThanOrEqualTo(MovementCount);

        var after = await ReadProjectionAsync(fixture);

        after.Should().BeEquivalentTo(
            before,
            "replaying the ledger, in the order it was posted, must reproduce the projection exactly");
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

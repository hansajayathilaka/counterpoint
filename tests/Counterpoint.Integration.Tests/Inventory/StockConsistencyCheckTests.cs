using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// The startup consistency check samples the projection against the ledger it is meant to
/// mirror (P1-T07, SAD §3).
/// </summary>
public sealed class StockConsistencyCheckTests
{
    [Fact]
    public async Task P1_T07_ACleanProjectionReportsNoMismatch()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var report = await fixture.Resolve<IStockConsistencyCheck>().CheckAsync(sampleSize: 50);

        report.SampledCount.Should().Be(1, "only the seeded variant has ever moved");
        report.HasMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task P1_T07_ADriftedProjectionIsFlaggedAsAMismatch()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // A balance the ledger cannot account for - never how production code writes it
        // (CLAUDE.md invariant 3) - is exactly the failure mode this check exists to catch.
        var connection = await fixture.OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE stock_balance SET qty_base = qty_base + 999999;";
            await command.ExecuteNonQueryAsync();
        }

        var report = await fixture.Resolve<IStockConsistencyCheck>().CheckAsync(sampleSize: 50);

        report.HasMismatch.Should().BeTrue();
        report.Mismatches.Should().ContainSingle();

        // The corruption above added 999999 to the *scaled* storage column, i.e. 99.9999 real
        // units (CLAUDE.md invariant 1). The reported mismatch must be in the same base-unit
        // decimals IStockConsistencyCheck's own XML doc promises, not the raw scaled integer -
        // otherwise a real 99.9999-unit drift would be misreported as a 999999-unit one.
        var mismatch = report.Mismatches.Single();
        (mismatch.ProjectedQtyBase - mismatch.LedgerQtyBase).Should().Be(99.9999m);
    }
}

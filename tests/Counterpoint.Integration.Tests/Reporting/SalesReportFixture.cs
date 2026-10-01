using System.Threading.Tasks;
using Counterpoint.Integration.Tests.Sales;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Builds <see cref="SalesReportDataset"/> once over a real encrypted SQLite file and shares it
/// with every read-only P3-T05 report test in the <see cref="Name"/> collection. A test that
/// changes the data (a cost change, a cashier sign-in) builds its own fixture instead.
/// </summary>
public sealed class SalesReportFixture : IAsyncLifetime
{
    /// <summary>The xunit collection every read-only report test joins.</summary>
    public const string Name = "P3-T05 fixed sales dataset";

    internal SaleFixture Host { get; private set; } = null!;

    internal SalesReportDataset Data { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        Data = await SalesReportDataset.BuildAsync(Host);
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

[CollectionDefinition(SalesReportFixture.Name)]
public sealed class SalesReportFixtureDefinition : ICollectionFixture<SalesReportFixture>
{
}

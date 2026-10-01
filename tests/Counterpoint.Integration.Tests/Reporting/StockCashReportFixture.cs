using System.Threading.Tasks;
using Counterpoint.Integration.Tests.Sales;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Builds <see cref="StockCashDataset"/> (the P3-T05 hand-worked trading history plus the P3-T06 stock-side
/// history) once over a real encrypted SQLite file and shares it with every read-only P3-T06 report test in
/// the <see cref="Name"/> collection. It is its own fixture, deliberately: the P3-T05 collection's dataset is
/// left exactly as that task's tests expect it. A test that changes the data builds its own fixture instead.
/// </summary>
public sealed class StockCashReportFixture : IAsyncLifetime
{
    /// <summary>The xunit collection every read-only P3-T06 report test joins.</summary>
    public const string Name = "P3-T06 stock and cash dataset";

    internal SaleFixture Host { get; private set; } = null!;

    internal StockCashDataset Data { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        Data = await StockCashDataset.BuildAsync(Host);
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

[CollectionDefinition(StockCashReportFixture.Name)]
public sealed class StockCashReportFixtureDefinition : ICollectionFixture<StockCashReportFixture>
{
}

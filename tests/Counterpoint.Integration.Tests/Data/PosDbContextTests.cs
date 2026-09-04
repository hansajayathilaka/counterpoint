using System.Linq;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// snake_case naming is configured once, in OnModelCreating (engineering guide §5).
/// </summary>
public sealed class PosDbContextTests
{
    [Fact]
    public void ModelUsesSnakeCaseTablesAndColumns()
    {
        using var fixture = new TemporaryDataDirectory();
        using var factory = fixture.CreateConnectionFactory();

        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(factory.OpenConfiguredConnection(), contextOwnsConnection: true)
            .Options;

        using var context = new PosDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(SchemaVersion));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("schema_version");

        entityType!.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(["version", "applied_at"]);
    }

    [Theory]
    [InlineData("SaleLine", "sale_line")]
    [InlineData("UomId", "uom_id")]
    [InlineData("QtyBase", "qty_base")]
    [InlineData("schema_version", "schema_version")]
    [InlineData("GRNLine", "grn_line")]
    public void ToSnakeCaseFollowsTheDatabaseNamingRule(string input, string expected) =>
        SnakeCaseNaming.ToSnakeCase(input).Should().Be(expected);
}

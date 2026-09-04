using System.Linq;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// The naming rule has to hold for the shapes Phase 1 actually uses, not just for the one flat
/// entity that exists today.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes are load-bearing. An <c>OwnsOne</c> on an owner with no explicit <c>ToTable</c>
/// shares the owner's table, and renaming its key and foreign key columns before EF has resolved
/// that sharing breaks model building outright. A <c>ComplexProperty</c> is how <c>Money</c> and
/// <c>Quantity</c> (CLAUDE.md invariant 1) get mapped from P1-T01 onwards, and a rename pass that
/// only walks <c>GetProperties()</c> misses them silently - PascalCase columns, no error.
/// </para>
/// <para>
/// The probe model lives here rather than in <see cref="PosDbContext"/> so that proving the rule
/// does not add tables to the till's schema.
/// </para>
/// </remarks>
public sealed class SnakeCaseNamingConventionTests
{
    [Fact]
    public void AnOwnedTypeOnAnUnnamedTableIsRenamedWithoutBreakingModelBuilding()
    {
        using var context = new NamingProbeContext();

        var owner = context.Model.FindEntityType(typeof(ProbeOrder));
        owner.Should().NotBeNull();
        owner!.GetTableName().Should().Be("probe_order");

        var owned = owner!.GetNavigations()
            .Single(navigation => navigation.Name == nameof(ProbeOrder.DeliveryAddress))
            .TargetEntityType;

        // Table splitting: the owned type lives in the owner's table, so it must carry the same
        // snake_case table name and share the owner's primary key column.
        owned.GetTableName().Should().Be("probe_order");
        owned.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(["order_id", "delivery_address_post_code", "delivery_address_street_line"]);
    }

    [Fact]
    public void ComplexPropertyColumnsAreRenamedToAnyDepth()
    {
        using var context = new NamingProbeContext();

        var owner = context.Model.FindEntityType(typeof(ProbeOrder));
        owner.Should().NotBeNull();

        // GetProperties() stops at the entity's own scalars - which is exactly why a rename pass
        // that only walks it leaves complex-type columns PascalCase with nothing to show for it.
        owner!.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(["order_id", "customer_name"]);

        var lineTotal = owner.GetComplexProperties().Single();
        lineTotal.ComplexType.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(["line_total_amount_minor"]);

        var currency = lineTotal.ComplexType.GetComplexProperties().Single();
        currency.ComplexType.GetProperties().Select(property => property.GetColumnName())
            .Should().BeEquivalentTo(
                ["line_total_currency_iso_code"],
                "the rename has to recurse, not stop at the first level of nesting");
    }

    private sealed class ProbeOrder
    {
        public int OrderId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public ProbeAddress DeliveryAddress { get; set; } = new();

        public ProbeMoney LineTotal { get; set; }
    }

    /// <summary>Mapped with <c>OwnsOne</c>: a separate entity type sharing the owner's table.</summary>
    private sealed class ProbeAddress
    {
        public string StreetLine { get; set; } = string.Empty;

        public string PostCode { get; set; } = string.Empty;
    }

    /// <summary>Mapped with <c>ComplexProperty</c>, the shape Money and Quantity will take.</summary>
    private struct ProbeMoney
    {
        public long AmountMinor { get; set; }

        public ProbeCurrency Currency { get; set; }
    }

    /// <summary>A complex type inside a complex type, to prove the rename recurses.</summary>
    private struct ProbeCurrency
    {
        public string IsoCode { get; set; }
    }

    /// <summary>
    /// No connection and no file: this asserts on the model EF builds, which is where the rule
    /// either holds or does not.
    /// </summary>
    private sealed class NamingProbeContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeOrder>(entity =>
            {
                // No ToTable on purpose. That is the shape in which renaming a shared column too
                // early takes model building down.
                entity.HasKey(order => order.OrderId);
                entity.OwnsOne(order => order.DeliveryAddress);
                entity.ComplexProperty(
                    order => order.LineTotal,
                    lineTotal => lineTotal.ComplexProperty(money => money.Currency));
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
    }
}

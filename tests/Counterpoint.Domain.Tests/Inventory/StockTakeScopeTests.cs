using System;
using Counterpoint.Domain.Inventory;
using FluentAssertions;
using Xunit;

namespace Counterpoint.Domain.Tests.Inventory;

/// <summary>
/// <see cref="StockTakeScope"/> - the pure scope-parsing rule <c>StockTakeService.StartAsync</c>
/// and <c>SqliteStockTakeStore.StartAsync</c> both depend on (SRS FR-4 stock take, AC-10, task
/// P2-T10 "start a stock take with a scope: all, category, brand or rack location"). Pure and
/// framework-free, so every parsing edge is provable without a database.
/// </summary>
public sealed class StockTakeScopeTests
{
    [Fact]
    public void FR_4_ParsesAllCaseInsensitively()
    {
        StockTakeScope.Parse("all").Should().Be(new StockTakeScope(StockTakeScopeKind.All, null, null));
        StockTakeScope.Parse("ALL").Kind.Should().Be(StockTakeScopeKind.All);
    }

    [Fact]
    public void FR_4_ParsesCategoryWithAPositiveIntegerId()
    {
        var scope = StockTakeScope.Parse("CATEGORY:12");

        scope.Kind.Should().Be(StockTakeScopeKind.Category);
        scope.Id.Should().Be(12);
    }

    [Fact]
    public void FR_4_ParsesBrandWithAPositiveIntegerId()
    {
        var scope = StockTakeScope.Parse("BRAND:5");

        scope.Kind.Should().Be(StockTakeScopeKind.Brand);
        scope.Id.Should().Be(5);
    }

    [Fact]
    public void FR_4_ParsesLocationWithAnAlphanumericRackId()
    {
        // Unlike CATEGORY/BRAND, a rack or bin id is not necessarily numeric ("A3", "AISLE-7")
        // - the parser must not impose the sibling scopes' integer-only rule here.
        var scope = StockTakeScope.Parse("LOCATION:A3");

        scope.Kind.Should().Be(StockTakeScopeKind.Location);
        scope.Location.Should().Be("A3");
        scope.Id.Should().BeNull();
    }

    [Fact]
    public void FR_4_ParsesLocationCaseInsensitivelyButPreservesTheRackIdsOwnCasing()
    {
        var scope = StockTakeScope.Parse("location:Aisle-7b");

        scope.Kind.Should().Be(StockTakeScopeKind.Location);
        scope.Location.Should().Be("Aisle-7b", "the keyword is case-insensitive, but the rack id itself is shop vocabulary and must round-trip exactly");
    }

    [Theory]
    [InlineData("ALL")]
    [InlineData("CATEGORY:12")]
    [InlineData("BRAND:5")]
    [InlineData("LOCATION:A3")]
    public void FR_4_ToTokenRoundTripsTheCanonicalUpperCaseForm(string canonical)
    {
        StockTakeScope.Parse(canonical).ToToken().Should().Be(canonical);
    }

    [Fact]
    public void FR_4_ToTokenCanonicalisesALowerCaseKeywordToUpperCase()
    {
        // "a shop keyer's capitalisation should not be able to fail a stock take" (the type's own
        // remarks) - but two scopes that mean the same thing must still be stored identically.
        StockTakeScope.Parse("category:12").ToToken().Should().Be("CATEGORY:12");
        StockTakeScope.Parse("brand:5").ToToken().Should().Be("BRAND:5");
        StockTakeScope.Parse("location:a3").ToToken().Should().Be("LOCATION:a3", "only the keyword is canonicalised - the rack id itself is not upper-cased");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FR_4_RejectsBlankScope(string raw)
    {
        var act = () => StockTakeScope.Parse(raw);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("GARBAGE")]
    [InlineData("EVERYTHING")]
    [InlineData("CATEGORY")]
    [InlineData("BRAND")]
    public void FR_4_RejectsAnUnrecognisedScopeKeyword(string raw)
    {
        var act = () => StockTakeScope.Parse(raw);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ALL, CATEGORY:<id>, BRAND:<id> or LOCATION:<rack>*");
    }

    [Theory]
    [InlineData("CATEGORY:0")]
    [InlineData("CATEGORY:-1")]
    [InlineData("CATEGORY:abc")]
    [InlineData("BRAND:0")]
    [InlineData("BRAND:xyz")]
    public void FR_4_RejectsACategoryOrBrandIdThatIsNotAPositiveInteger(string raw)
    {
        var act = () => StockTakeScope.Parse(raw);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FR_4_RejectsALocationScopeWithNoRackAfterTheColon()
    {
        var act = () => StockTakeScope.Parse("LOCATION:");

        act.Should().Throw<ArgumentException>().WithMessage("*rack or bin*");
    }
}

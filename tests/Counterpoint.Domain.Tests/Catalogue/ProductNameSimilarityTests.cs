using Counterpoint.Domain.Catalogue;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>
/// The text-similarity half of the duplicate-on-creation warning (SRS FR-2.24).
/// </summary>
public sealed class ProductNameSimilarityTests
{
    [Fact]
    public void IdenticalNamesScoreOne()
    {
        ProductNameSimilarity.Score("Bosch Drill 13mm", "Bosch Drill 13mm").Should().Be(1m);
    }

    [Fact]
    public void FR_2_24_ACaseAndWhitespaceVariantOfTheSameNameIsVerySimilar()
    {
        ProductNameSimilarity.AreSimilar("  bosch   drill 13mm", "BOSCH DRILL 13MM").Should().BeTrue();
    }

    [Fact]
    public void FR_2_24_ATypoOfAnExistingNameIsVerySimilar()
    {
        // One missing letter in a sixteen-character name is well inside the default threshold.
        ProductNameSimilarity.AreSimilar("Bosch Drill 13mm", "Bosch Drll 13mm").Should().BeTrue();
    }

    [Fact]
    public void FR_2_24_ADifferentProductIsNotSimilar()
    {
        ProductNameSimilarity.AreSimilar("Bosch Drill 13mm", "Galvanised Bolt M8").Should().BeFalse();
    }

    [Fact]
    public void TwoBlankNamesAreIdentical()
    {
        ProductNameSimilarity.Score(string.Empty, "   ").Should().Be(1m);
    }
}

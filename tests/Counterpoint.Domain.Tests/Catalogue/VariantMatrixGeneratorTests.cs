using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.Catalogue;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>
/// Generating a variant matrix in one operation, skipping combinations that already exist (SRS
/// FR-2.6, §2.2's ten-length-by-two-thread-by-three-finish case).
/// </summary>
public sealed class VariantMatrixGeneratorTests
{
    [Fact]
    public void FR_2_6_GeneratesSixtyVariantsFromTenLengthsByTwoThreadsByThreeFinishes()
    {
        var axes = new[]
        {
            new VariantAxis("length", Enumerable.Range(1, 10).Select(i => i + "mm").ToArray()),
            new VariantAxis("thread", ["M8", "M10"]),
            new VariantAxis("finish", ["Zinc", "Black", "Stainless"]),
        };

        var generated = VariantMatrixGenerator.Generate(axes, existing: []);

        generated.Should().HaveCount(60, "10 lengths x 2 threads x 3 finishes (SRS §2.2)");
        generated.Distinct().Should().HaveCount(60, "every combination must be distinct");
    }

    [Fact]
    public void FR_2_6_SkipsCombinationsThatAlreadyHaveAVariant()
    {
        var axes = new[]
        {
            new VariantAxis("size", ["S", "M", "L"]),
            new VariantAxis("colour", ["Red", "Blue"]),
        };

        var existing = new[]
        {
            new VariantAttributes(CatalogueTestBuilder.Attributes(("size", "S"), ("colour", "Red"))),
        };

        var generated = VariantMatrixGenerator.Generate(axes, existing);

        generated.Should().HaveCount(5, "one of the six combinations already exists");
        generated.Should().NotContain(existing[0]);
    }

    [Fact]
    public void FR_2_6_ExistingCombinationsAreMatchedRegardlessOfAttributeKeyOrder()
    {
        var axes = new[]
        {
            new VariantAxis("size", ["S"]),
            new VariantAxis("colour", ["Red"]),
        };

        // The stored JSON on the existing variant lists colour before size - a different key
        // order to the axes above, which must not defeat the duplicate check.
        var existing = new[]
        {
            new VariantAttributes(CatalogueTestBuilder.Attributes(("colour", "Red"), ("size", "S"))),
        };

        var generated = VariantMatrixGenerator.Generate(axes, existing);

        generated.Should().BeEmpty("the single combination already exists, key order notwithstanding");
    }

    [Fact]
    public void FR_2_6_EveryGeneratedCombinationCarriesEveryAxis()
    {
        var axes = new[]
        {
            new VariantAxis("size", ["S", "M"]),
            new VariantAxis("colour", ["Red"]),
        };

        var generated = VariantMatrixGenerator.Generate(axes, existing: []);

        generated.Should().OnlyContain(combination => combination.Pairs.Count == 2);
        generated.Select(c => c.ToDictionary()["size"]).Should().BeEquivalentTo(["S", "M"]);
        generated.Should().OnlyContain(combination => combination.ToDictionary()["colour"] == "Red");
    }

    [Fact]
    public void FR_2_6_RejectsInputThatCannotBeGenerated()
    {
        Action noAxes = () => VariantMatrixGenerator.Generate([], existing: []);
        Action blankAxisName = () => VariantMatrixGenerator.Generate(
            [new VariantAxis(" ", ["a"])], existing: []);
        Action duplicateAxisName = () => VariantMatrixGenerator.Generate(
            [new VariantAxis("size", ["S"]), new VariantAxis("size", ["M"])], existing: []);
        Action emptyAxisValues = () => VariantMatrixGenerator.Generate(
            [new VariantAxis("size", [])], existing: []);

        noAxes.Should().Throw<ArgumentException>();
        blankAxisName.Should().Throw<ArgumentException>();
        duplicateAxisName.Should().Throw<ArgumentException>();
        emptyAxisValues.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FR_2_6_ASingleAxisGeneratesOneVariantPerValue()
    {
        var axes = new[] { new VariantAxis("size", ["S", "M", "L", "XL"]) };

        var generated = VariantMatrixGenerator.Generate(axes, existing: []);

        generated.Should().HaveCount(4);
    }

    [Fact]
    public void FR_2_6_EveryCombinationAlreadyExistingGeneratesNothing()
    {
        var axes = new[] { new VariantAxis("size", ["S", "M"]) };

        var existing = new List<VariantAttributes>
        {
            new(CatalogueTestBuilder.Attributes(("size", "S"))),
            new(CatalogueTestBuilder.Attributes(("size", "M"))),
        };

        var generated = VariantMatrixGenerator.Generate(axes, existing);

        generated.Should().BeEmpty();
    }
}

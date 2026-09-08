using System;
using Counterpoint.Domain.Catalogue;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>
/// Internal barcode generation for loose or unbarcoded items (SRS FR-2.10).
/// </summary>
public sealed class InternalBarcodeGeneratorTests
{
    [Fact]
    public void FR_2_10_GeneratedBarcodeIsPrefixPlusPaddedSerialPlusAValidCheckDigit()
    {
        var barcode = InternalBarcodeGenerator.Generate("20", 42);

        barcode.Should().Be("2000000000428", "prefix, a 10-digit zero-padded serial, then the check digit");
        InternalBarcodeGenerator.IsValid(barcode).Should().BeTrue();
    }

    [Fact]
    public void FR_2_10_TamperingWithAnyDigitInvalidatesTheCheckDigit()
    {
        var barcode = InternalBarcodeGenerator.Generate("20", 42);
        var tampered = string.Concat(barcode[..^2], barcode[^2] == '0' ? '1' : '0', barcode[^1]);

        InternalBarcodeGenerator.IsValid(tampered).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(1234567)]
    [InlineData(9_999_999_999)]
    public void FR_2_10_EverySerialRoundTripsThroughItsOwnCheckDigit(long serial)
    {
        var barcode = InternalBarcodeGenerator.Generate("29", serial);

        InternalBarcodeGenerator.IsValid(barcode).Should().BeTrue();
        barcode.Should().HaveLength(2 + InternalBarcodeGenerator.SerialWidth + 1);
    }

    [Fact]
    public void FR_2_10_DifferentPrefixesNeverCollideForTheSameSerial()
    {
        var first = InternalBarcodeGenerator.Generate("20", 1);
        var second = InternalBarcodeGenerator.Generate("21", 1);

        first.Should().NotBe(second);
    }

    [Fact]
    public void APrefixThatIsNotDigitsOnlyIsRejected()
    {
        var generate = () => InternalBarcodeGenerator.Generate("IN", 1);

        generate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ANegativeSerialIsRejected()
    {
        var generate = () => InternalBarcodeGenerator.Generate("20", -1);

        generate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("ABCDE12345")]
    [InlineData("123456789X")]
    public void IsValidRejectsAnythingThatIsNotADigitsOnlyCheckedCode(string? candidate)
    {
        InternalBarcodeGenerator.IsValid(candidate).Should().BeFalse();
    }
}

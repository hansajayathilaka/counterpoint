using System;
using System.Globalization;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// Generates and validates internal barcodes for loose or unbarcoded items (SRS FR-2.10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Digits only, with a trailing check digit.</b> The code is a shop-chosen numeric prefix
/// followed by a zero-padded serial and a single check digit computed with the same weighted
/// mod-10 algorithm UPC-A and EAN-13 use (weight 3 on every digit an odd number of places from
/// the right, weight 1 otherwise). That is not because this shop trades in UPC or EAN codes -
/// it does not, these are internal-only - but because the algorithm is well understood, cheap to
/// verify by hand at the till if a scan ever looks wrong, and printable as Code 128
/// (<c>Counterpoint.Devices.Printing.BarcodeSymbology.Code128</c>, P1-T12), which encodes any
/// ASCII string a digits-only code is a trivial case of.
/// </para>
/// <para>
/// Pure and stateless. Where the serial itself comes from - a gapless, non-reused counter - is
/// an infrastructure concern (<c>number_sequence</c>, CLAUDE.md invariant 4); this type only
/// knows how to turn a prefix and a serial into a barcode and back.
/// </para>
/// </remarks>
public static class InternalBarcodeGenerator
{
    /// <summary>The serial's printed width before the check digit, zero-padded.</summary>
    public const int SerialWidth = 10;

    /// <summary>
    /// Builds the full barcode: <paramref name="prefix"/>, the serial zero-padded to
    /// <see cref="SerialWidth"/> digits, and a trailing check digit.
    /// </summary>
    /// <param name="prefix">Digits only, 1-8 characters - the shop's configured internal prefix.</param>
    /// <param name="serial">The next value out of the serial's number sequence. Never negative.</param>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is empty, too long, or not all digits.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="serial"/> is negative or does not fit <see cref="SerialWidth"/> digits.</exception>
    public static string Generate(string prefix, long serial)
    {
        RequireDigitPrefix(prefix);

        if (serial < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serial), serial, "A barcode serial cannot be negative.");
        }

        var serialText = serial.ToString(CultureInfo.InvariantCulture);
        if (serialText.Length > SerialWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serial), serial, $"A barcode serial cannot exceed {SerialWidth} digits.");
        }

        var body = prefix + serialText.PadLeft(SerialWidth, '0');
        return body + ComputeCheckDigit(body).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// True when <paramref name="barcode"/> is all digits and its last digit is the correct
    /// check digit for the digits before it.
    /// </summary>
    public static bool IsValid(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode) || barcode.Length < 2 || !IsAllDigits(barcode))
        {
            return false;
        }

        var body = barcode[..^1];
        var checkDigit = barcode[^1] - '0';

        return ComputeCheckDigit(body) == checkDigit;
    }

    /// <summary>
    /// The UPC/EAN-style mod-10 check digit for <paramref name="digits"/>: every digit at an odd
    /// position counting from the rightmost digit (1-indexed) is weighted 3, every digit at an
    /// even position is weighted 1, and the check digit is whatever brings the weighted sum to
    /// the next multiple of ten.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="digits"/> is empty or not all digits.</exception>
    public static int ComputeCheckDigit(string digits)
    {
        if (string.IsNullOrEmpty(digits) || !IsAllDigits(digits))
        {
            throw new ArgumentException("A check digit can only be computed over a non-empty string of digits.", nameof(digits));
        }

        var sum = 0;
        var weightThree = true; // the rightmost digit (position 1, odd) carries weight 3.

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var value = digits[i] - '0';
            sum += weightThree ? value * 3 : value;
            weightThree = !weightThree;
        }

        var remainder = sum % 10;
        return remainder == 0 ? 0 : 10 - remainder;
    }

    private static void RequireDigitPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (prefix.Length > 8)
        {
            throw new ArgumentException("An internal barcode prefix cannot exceed 8 digits - it must leave room for a serial and a check digit.", nameof(prefix));
        }

        if (!IsAllDigits(prefix))
        {
            throw new ArgumentException("An internal barcode prefix must be digits only.", nameof(prefix));
        }
    }

    private static bool IsAllDigits(string text)
    {
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

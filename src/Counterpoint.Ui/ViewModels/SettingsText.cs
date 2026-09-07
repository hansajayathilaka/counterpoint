using System;
using System.Globalization;
using System.Text;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// Turns what somebody typed into a value object, and a value object back into what they see.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here decides anything.</b> It parses and it formats. Whether 150% is an acceptable
/// discount ceiling is <c>SettingsValidation</c>'s business, in the Application layer, and it
/// says so in a sentence the owner reads (CLAUDE.md invariant 8, SRS UI-06).
/// </para>
/// <para>
/// <b>No amount is ever rounded here.</b> A settings box holds a number the owner typed;
/// rounding happens at the line total and the bill total, through <c>IRoundingPolicy</c>, and
/// nowhere else (CLAUDE.md invariant 2). <see cref="FromDecimal"/> formats to four places
/// because that is what the scaled-integer storage can represent - it is a display width, not a
/// rounding point.
/// </para>
/// <para>
/// Parsing is culture-invariant with <c>.</c> as the point, and
/// <see cref="Sanitise"/> accepts the current culture's separator by rewriting it. A numeric
/// keypad on a shop counter types <c>.</c>, and a setting that means one thing on one machine's
/// locale and another on the next is a bug waiting for a holiday.
/// </para>
/// </remarks>
internal static class SettingsText
{
    /// <summary>The one decimal point this file parses and writes.</summary>
    private const char DecimalPoint = '.';

    /// <summary>
    /// As many characters as any settings number could honestly need. Beyond it the keystroke is
    /// dropped, rather than parsed into something that overflows and silently becomes zero.
    /// </summary>
    private const int MaximumLength = 18;

    /// <summary>
    /// Everything in <paramref name="typed"/> that could be part of a number, and nothing else
    /// (SRS UI-10: reject invalid characters silently - no dialog while a customer waits).
    /// </summary>
    internal static string Sanitise(string? typed, bool allowDecimal, bool allowNegative)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return string.Empty;
        }

        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var builder = new StringBuilder(typed.Length);
        var pointSeen = false;

        foreach (var character in typed)
        {
            if (builder.Length >= MaximumLength)
            {
                break;
            }

            if (char.IsAsciiDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (allowNegative && character == '-' && builder.Length == 0)
            {
                builder.Append(character);
                continue;
            }

            var isPoint = character == DecimalPoint
                || (separator.Length == 1 && character == separator[0]);

            if (allowDecimal && isPoint && !pointSeen)
            {
                builder.Append(DecimalPoint);
                pointSeen = true;
            }

            // Anything else is a stray keystroke at a counter. Dropped, without a word.
        }

        return builder.ToString();
    }

    /// <summary>Keeps only what a clock time can be made of.</summary>
    internal static string SanitiseTime(string? typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(typed.Length);

        foreach (var character in typed)
        {
            if (builder.Length >= 5)
            {
                break;
            }

            if (char.IsAsciiDigit(character) || character == ':')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    internal static decimal ToDecimal(string? text, decimal fallback = 0m) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal static string FromDecimal(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    internal static Money ToMoney(string? text) => Money.FromDecimal(ToDecimal(text));

    internal static string FromMoney(Money money) => FromDecimal(money.Amount);

    /// <summary>Reads a box holding percent - <c>12.5</c> is 12.5%.</summary>
    internal static Percentage ToPercentage(string? text) => Percentage.FromPercent(ToDecimal(text));

    internal static string FromPercentage(Percentage percentage) => FromDecimal(percentage.AsPercent);

    /// <summary>Reads a box holding a tax rate in percent. A rate can never be negative.</summary>
    internal static TaxRate ToTaxRate(string? text) => TaxRate.FromPercent(Math.Max(ToDecimal(text), 0m));

    internal static string FromTaxRate(TaxRate rate) => FromDecimal(rate.AsPercent);

    internal static int ToInt(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal static string FromInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static long ToLong(string? text, long fallback) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal static string FromLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal static bool TryToTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            text,
            ["HH:mm", "H:mm", "HHmm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    internal static string FromTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

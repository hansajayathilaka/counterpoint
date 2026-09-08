using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// Refuses a settings value the shop could not actually trade on (SRS FR-10, UI-06).
/// </summary>
/// <remarks>
/// Checked on the way in, before anything is written, so a bad value never reaches the database
/// and never has to be corrected afterwards. The messages are the ones a shopkeeper reads, not a
/// developer: "a value between 0 and 4", not "argument out of range".
/// </remarks>
public static class SettingsValidation
{
    /// <summary>The most decimal places the scaled-integer storage can represent.</summary>
    public const int MaxDecimalPlaces = Money.MoneyDecimalPlaces;

    /// <summary>Throws if any value in <paramref name="snapshot"/> is unusable.</summary>
    /// <exception cref="ArgumentException">A value is outside what the shop can trade on.</exception>
    public static void Validate(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Range(
            snapshot.Financial.DecimalPlaces,
            0,
            MaxDecimalPlaces,
            "The currency's decimal places");
        Range(
            snapshot.Financial.QuantityDecimalPlaces,
            0,
            MaxDecimalPlaces,
            "The quantity decimal places");
        NotBlank(snapshot.Financial.CurrencyCode, "The currency code");

        NotBlank(snapshot.Tax.DefaultTaxClassName, "The default tax class name");
        NotBlank(snapshot.Tax.TaxLabel, "The name tax is printed under");

        Series(snapshot.Numbering.Bill, "bill");
        Series(snapshot.Numbering.Return, "return");
        Series(snapshot.Numbering.CreditNote, "credit note");
        Series(snapshot.Numbering.GoodsReceipt, "goods receipt");
        Series(snapshot.Numbering.PurchaseOrder, "purchase order");
        Series(snapshot.Numbering.Shift, "shift");

        Range(snapshot.Policy.ReturnWindowDays, 0, 3650, "The return window, in days");
        NotNegative(snapshot.Policy.CashRefundLimit, "The cash refund limit");
        Rate(snapshot.Policy.MaxLineDiscountRate, "The line discount limit");
        Rate(snapshot.Policy.MaxBillDiscountRate, "The bill discount limit");
        Rate(snapshot.Policy.RestockingFeeRate, "The restocking fee");

        Range(snapshot.Peripherals.PaperWidthMm, 1, 210, "The paper width, in millimetres");
        Range(snapshot.Peripherals.ReceiptCopies, 1, 9, "The number of receipt copies");
        DrawerPin(snapshot.Peripherals.DrawerKickPin);
        Range(snapshot.Peripherals.ScannerMinimumLength, 1, 64, "The shortest scan length");
        Range(snapshot.Peripherals.ScaleBaudRate, 300, 921_600, "The scale's baud rate");

        Range(snapshot.Backup.RetentionDays, 1, 3650, "How many days of backups are kept");
        Range(snapshot.Backup.RetentionCopies, 1, 10_000, "How many backup copies are kept");
    }

    private static void Series(DocumentNumbering series, string what)
    {
        if (string.IsNullOrWhiteSpace(series.Pattern))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.CurrentCulture, $"The {what} number needs a pattern."),
                nameof(series));
        }

        if (series.StartingNumber < 1)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"The first {what} number must be 1 or more; {series.StartingNumber} was given."),
                nameof(series));
        }
    }

    private static void Range(int value, int minimum, int maximum, string what)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{what} must be between {minimum} and {maximum}; {value} was given."),
                nameof(value));
        }
    }

    private static void Rate(Percentage rate, string what)
    {
        if (rate < Percentage.Zero || rate > Percentage.OneHundredPercent)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{what} must be between 0% and 100%; {rate} was given."),
                nameof(rate));
        }
    }

    private static void NotNegative(Money amount, string what)
    {
        if (amount.IsNegative)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.CurrentCulture, $"{what} cannot be a negative amount."),
                nameof(amount));
        }
    }

    private static void NotBlank(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.CurrentCulture, $"{what} cannot be blank."),
                nameof(value));
        }
    }

    private static void DrawerPin(int pin)
    {
        if (pin is not (2 or 5))
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"The cash drawer is wired to pin 2 or pin 5; {pin} was given."),
                nameof(pin));
        }
    }
}

using System;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The complete default set, covering SRS FR-10.1 through FR-10.8.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only place in the solution allowed to contain a currency symbol, a tax rate or
/// a discount limit.</b> Everything else reads them from <see cref="ISettings"/>, and
/// <c>NoHardCodedBusinessValuesTests</c> fails the build if that stops being true. A shop that
/// has never opened the settings screen trades on exactly these numbers, so they have to be the
/// safe answer rather than a placeholder.
/// </para>
/// <para>
/// A default is also the fallback for a row that is missing or unreadable. A corrupt
/// <c>app_setting</c> value must not stop the till starting, so
/// <see cref="SettingsSerializer.FromRows"/> falls back here and the shop trades on a known,
/// conservative value instead of on nothing.
/// </para>
/// </remarks>
public static class SettingDefaults
{
    /// <summary>
    /// Q-16's documented default bill pattern, and the one <c>FirstRunSeeder</c> already
    /// writes: <c>INV-2026-000001</c>.
    /// </summary>
    /// <remarks>
    /// <b>Q-16 is unanswered.</b> These defaults reproduce, exactly, what P0-T04/P0-T06 seed
    /// into <c>number_sequence</c>, so that taking the series over changes no existing number
    /// and no existing test. When the shop answers Q-16 the answer changes these values and the
    /// rows they seed - not any code.
    /// </remarks>
    public const string YearlyNumberPattern = "{prefix}{yyyy}-{n:000000}";

    /// <summary>A series not broken by year: <c>SH-000001</c>.</summary>
    public const string PlainNumberPattern = "{prefix}{n:000000}";

    /// <summary>The first number every series issues.</summary>
    private const long FirstNumber = 1;

    /// <summary>
    /// FR-10.1. Empty: the shop's own name, address and tax registration number are the first
    /// run's to supply, and a placeholder printed on a real bill would be worse than a blank line.
    /// </summary>
    public static ShopProfileSettings Shop { get; } = new(
        Name: string.Empty,
        AddressLine1: string.Empty,
        AddressLine2: string.Empty,
        Phone: string.Empty,
        Email: string.Empty,
        TaxRegistrationNumber: string.Empty,
        LogoPath: string.Empty);

    /// <summary>FR-10.2. Sri Lankan rupees at two decimal places, half away from zero (Q-01).</summary>
    public static FinancialSettings Financial { get; } = new(
        CurrencyCode: "LKR",
        CurrencySymbol: "Rs.",
        SymbolPosition: CurrencySymbolPosition.Before,
        DecimalPlaces: 2,
        RoundingRule: RoundingRule.HalfAwayFromZero,

        // Three places covers metres, kilograms and litres, which is every fractional UOM the
        // catalogue seeds (docs/01_DATA_MODEL.md §11).
        QuantityDecimalPlaces: 3);

    /// <summary>
    /// FR-10.3. One tax class, exempt, and tax-inclusive pricing - the shop's answer to Q-02 is
    /// "build it fully configurable, defer the regime". A rate invented here would be a wrong
    /// number printed on a bill.
    /// </summary>
    public static TaxSettings Tax { get; } = new(
        PricesIncludeTax: true,
        DefaultTaxClassName: "Exempt",
        DefaultTaxRate: TaxRate.Zero,
        TaxLabel: "Tax");

    /// <summary>FR-10.4. Exactly the series <c>FirstRunSeeder</c> seeds today (Q-16 pending).</summary>
    public static NumberingSettings Numbering { get; } = new(
        Bill: new DocumentNumbering("INV-", YearlyNumberPattern, FirstNumber),
        Return: new DocumentNumbering("RTN-", YearlyNumberPattern, FirstNumber),
        CreditNote: new DocumentNumbering("CN-", YearlyNumberPattern, FirstNumber),
        GoodsReceipt: new DocumentNumbering("GRN-", YearlyNumberPattern, FirstNumber),
        PurchaseOrder: new DocumentNumbering("PO-", YearlyNumberPattern, FirstNumber),
        Shift: new DocumentNumbering("SH-", PlainNumberPattern, FirstNumber));

    /// <summary>
    /// FR-10.5. Fourteen days to return with the bill (Q-03), negative stock allowed (Q-11), and
    /// no discount ceiling at all (Q-12, "not for now" - the limit exists, set to 100%, so that
    /// P1-T08 has something real to enforce the day the shop wants one).
    /// </summary>
    public static PolicySettings Policy { get; } = new(
        ReturnWindowDays: 14,
        AllowUnlinkedReturns: false,
        DefaultRefundMethod: RefundMethod.Cash,
        CashRefundLimit: Money.Zero,
        MaxLineDiscountRate: Percentage.OneHundredPercent,
        MaxBillDiscountRate: Percentage.OneHundredPercent,
        NegativeStock: NegativeStockPolicy.Allow,
        RestockingFeeRate: Percentage.Zero);

    /// <summary>
    /// FR-10.6. What the Linux fakes need, which is also what an uncommissioned Windows terminal
    /// needs: no named queue, 80 mm paper, one copy, drawer on pin 2. HW-T01..HW-T04 set the real
    /// values on site.
    /// </summary>
    public static PeripheralSettings Peripherals { get; } = new(
        ReceiptPrinterName: string.Empty,
        PaperWidthMm: 80,
        ReceiptCopies: 1,
        LabelPrinterName: string.Empty,
        OpenDrawerOnCashSale: true,
        DrawerKickPin: 2,
        ScannerSuffix: ScannerSuffix.Enter,
        ScannerMinimumLength: 4,
        ScaleEnabled: false,
        ScalePort: string.Empty,
        ScaleBaudRate: 9600);

    /// <summary>
    /// FR-10.7. A nightly backup after closing time, a copy on shift close, Google Drive as the
    /// off-site target (Q-D). No path is guessed: empty means the <c>backups</c> folder inside
    /// the data directory, and no USB copy until the owner names a drive.
    /// </summary>
    public static BackupSettings Backup { get; } = new(
        DailyBackupTime: new TimeOnly(20, 0),
        BackupOnShiftClose: true,
        LocalPath: string.Empty,
        UsbPath: string.Empty,
        CloudTarget: CloudBackupTarget.GoogleDrive,
        CloudAccount: string.Empty,
        RetentionDays: 30,
        RetentionCopies: 14,

        // Never persisted. Read from the protected store at load; false until one is set.
        PassphraseIsSet: false);

    /// <summary>
    /// FR-10.8. The wording of the SRS §10.1 specimen bill, which is what the shop sees printed
    /// before it changes a word of it.
    /// </summary>
    public static ReceiptSettings Receipt { get; } = new(
        HeaderText: string.Empty,
        FooterText: "Thank you - please come again",
        PolicyText:
            "Returns accepted within 14 days with this bill. Cut goods & mixed paint are "
            + "non-returnable.",
        ShowLogo: false,
        ShowBillBarcode: true,
        ShowCashierName: true,
        ShowCustomerName: true,
        ShowItemAndUnitCount: true,
        ShowTaxSummary: true,
        ShowTaxableValue: true,
        ShowTaxRegistrationNumber: true);

    /// <summary>
    /// The whole default set. An immutable value, so handing it out costs nothing and nobody can
    /// mutate the defaults from under the next reader.
    /// </summary>
    /// <remarks>
    /// Declared last on purpose: static property initialisers run in declaration order, so this
    /// one has to come after the eight groups it is built from.
    /// </remarks>
    public static SettingsSnapshot Snapshot { get; } = new(
        Shop,
        Financial,
        Tax,
        Numbering,
        Policy,
        Peripherals,
        Backup,
        Receipt);
}

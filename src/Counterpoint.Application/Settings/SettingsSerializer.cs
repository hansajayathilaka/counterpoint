using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The one place a typed setting becomes an <c>app_setting</c> row and back
/// (SRS FR-10.1-10.8, docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a flat, explicit list rather than reflection over the record types. The mapping
/// between a C# property and a key in the shop's database is the thing that must not move when
/// somebody renames a property, so it is written down once and a rename breaks the build here
/// instead of silently reverting a shop's setting to its default.
/// </para>
/// <para>
/// <b>Reading never throws.</b> A missing row, an unparseable number or an unknown enum token
/// falls back to <see cref="SettingDefaults"/>. One corrupt value must not stop the till trading;
/// it degrades to the conservative default, exactly as a peripheral failure degrades
/// (CLAUDE.md invariant 7).
/// </para>
/// </remarks>
public static class SettingsSerializer
{
    /// <summary>
    /// Flattens a snapshot into the rows that represent it. Every FR-10.1-10.8 setting appears
    /// exactly once; <c>BackupSettings.PassphraseIsSet</c> deliberately does not, because the
    /// passphrase itself never goes near this table.
    /// </summary>
    public static IReadOnlyList<SettingRow> ToRows(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rows = new List<SettingRow>(80);

        AppendShop(rows, snapshot.Shop);
        AppendFinancial(rows, snapshot.Financial);
        AppendTax(rows, snapshot.Tax);
        AppendNumbering(rows, snapshot.Numbering);
        AppendPolicy(rows, snapshot.Policy);
        AppendPeripherals(rows, snapshot.Peripherals);
        AppendBackup(rows, snapshot.Backup);
        AppendReceipt(rows, snapshot.Receipt);

        return rows;
    }

    /// <summary>
    /// Rebuilds a snapshot from what is on disk, falling back to <paramref name="fallback"/> for
    /// anything missing or unreadable.
    /// </summary>
    /// <param name="rows">Every <c>app_setting</c> row, keyed by <c>key</c>.</param>
    /// <param name="fallback">
    /// Normally <see cref="SettingDefaults.Snapshot"/>. Taken as a parameter so a test can prove
    /// the fallback is used rather than assume it.
    /// </param>
    public static SettingsSnapshot FromRows(
        IReadOnlyDictionary<string, StoredSetting> rows,
        SettingsSnapshot fallback)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(fallback);

        return new SettingsSnapshot(
            ReadShop(rows, fallback.Shop),
            ReadFinancial(rows, fallback.Financial),
            ReadTax(rows, fallback.Tax),
            ReadNumbering(rows, fallback.Numbering),
            ReadPolicy(rows, fallback.Policy),
            ReadPeripherals(rows, fallback.Peripherals),
            ReadBackup(rows, fallback.Backup),
            ReadReceipt(rows, fallback.Receipt));
    }

    // ---- FR-10.1 Shop profile ----------------------------------------------------------------

    private static void AppendShop(List<SettingRow> rows, ShopProfileSettings shop)
    {
        rows.Add(Text(SettingKeys.ShopName, shop.Name));
        rows.Add(Text(SettingKeys.ShopAddressLine1, shop.AddressLine1));
        rows.Add(Text(SettingKeys.ShopAddressLine2, shop.AddressLine2));
        rows.Add(Text(SettingKeys.ShopPhone, shop.Phone));
        rows.Add(Text(SettingKeys.ShopEmail, shop.Email));
        rows.Add(Text(SettingKeys.ShopTaxRegistrationNumber, shop.TaxRegistrationNumber));
        rows.Add(Text(SettingKeys.ShopLogoPath, shop.LogoPath));
    }

    private static ShopProfileSettings ReadShop(
        IReadOnlyDictionary<string, StoredSetting> rows,
        ShopProfileSettings fallback) => new(
            ReadText(rows, SettingKeys.ShopName, fallback.Name),
            ReadText(rows, SettingKeys.ShopAddressLine1, fallback.AddressLine1),
            ReadText(rows, SettingKeys.ShopAddressLine2, fallback.AddressLine2),
            ReadText(rows, SettingKeys.ShopPhone, fallback.Phone),
            ReadText(rows, SettingKeys.ShopEmail, fallback.Email),
            ReadText(rows, SettingKeys.ShopTaxRegistrationNumber, fallback.TaxRegistrationNumber),
            ReadText(rows, SettingKeys.ShopLogoPath, fallback.LogoPath));

    // ---- FR-10.2 Financial -------------------------------------------------------------------

    private static void AppendFinancial(List<SettingRow> rows, FinancialSettings financial)
    {
        rows.Add(Text(SettingKeys.FinancialCurrencyCode, financial.CurrencyCode));
        rows.Add(Text(SettingKeys.FinancialCurrencySymbol, financial.CurrencySymbol));
        rows.Add(Text(SettingKeys.FinancialCurrencySymbolPosition, SettingTokens.From(financial.SymbolPosition)));
        rows.Add(Integer(SettingKeys.FinancialDecimalPlaces, financial.DecimalPlaces));
        rows.Add(Text(SettingKeys.FinancialRoundingRule, SettingTokens.From(financial.RoundingRule)));
        rows.Add(Integer(SettingKeys.FinancialQuantityDecimalPlaces, financial.QuantityDecimalPlaces));
    }

    private static FinancialSettings ReadFinancial(
        IReadOnlyDictionary<string, StoredSetting> rows,
        FinancialSettings fallback) => new(
            ReadText(rows, SettingKeys.FinancialCurrencyCode, fallback.CurrencyCode),
            ReadText(rows, SettingKeys.FinancialCurrencySymbol, fallback.CurrencySymbol),
            SettingTokens.ToSymbolPosition(
                ReadText(rows, SettingKeys.FinancialCurrencySymbolPosition, string.Empty),
                fallback.SymbolPosition),
            ReadInt(rows, SettingKeys.FinancialDecimalPlaces, fallback.DecimalPlaces),
            SettingTokens.ToRoundingRule(
                ReadText(rows, SettingKeys.FinancialRoundingRule, string.Empty),
                fallback.RoundingRule),
            ReadInt(rows, SettingKeys.FinancialQuantityDecimalPlaces, fallback.QuantityDecimalPlaces));

    // ---- FR-10.3 Tax -------------------------------------------------------------------------

    private static void AppendTax(List<SettingRow> rows, TaxSettings tax)
    {
        rows.Add(Boolean(SettingKeys.TaxPricesIncludeTax, tax.PricesIncludeTax));
        rows.Add(Text(SettingKeys.TaxDefaultClassName, tax.DefaultTaxClassName));
        rows.Add(Scaled(SettingKeys.TaxDefaultRate, tax.DefaultTaxRate.ToScaled()));
        rows.Add(Text(SettingKeys.TaxLabel, tax.TaxLabel));
    }

    private static TaxSettings ReadTax(
        IReadOnlyDictionary<string, StoredSetting> rows,
        TaxSettings fallback) => new(
            ReadBool(rows, SettingKeys.TaxPricesIncludeTax, fallback.PricesIncludeTax),
            ReadText(rows, SettingKeys.TaxDefaultClassName, fallback.DefaultTaxClassName),
            ReadTaxRate(rows, SettingKeys.TaxDefaultRate, fallback.DefaultTaxRate),
            ReadText(rows, SettingKeys.TaxLabel, fallback.TaxLabel));

    // ---- FR-10.4 Numbering -------------------------------------------------------------------

    private static void AppendNumbering(List<SettingRow> rows, NumberingSettings numbering)
    {
        AppendSeries(
            rows,
            SettingKeys.NumberingBillPrefix,
            SettingKeys.NumberingBillPattern,
            SettingKeys.NumberingBillStartingNumber,
            numbering.Bill);
        AppendSeries(
            rows,
            SettingKeys.NumberingReturnPrefix,
            SettingKeys.NumberingReturnPattern,
            SettingKeys.NumberingReturnStartingNumber,
            numbering.Return);
        AppendSeries(
            rows,
            SettingKeys.NumberingCreditNotePrefix,
            SettingKeys.NumberingCreditNotePattern,
            SettingKeys.NumberingCreditNoteStartingNumber,
            numbering.CreditNote);
        AppendSeries(
            rows,
            SettingKeys.NumberingGoodsReceiptPrefix,
            SettingKeys.NumberingGoodsReceiptPattern,
            SettingKeys.NumberingGoodsReceiptStartingNumber,
            numbering.GoodsReceipt);
        AppendSeries(
            rows,
            SettingKeys.NumberingPurchaseOrderPrefix,
            SettingKeys.NumberingPurchaseOrderPattern,
            SettingKeys.NumberingPurchaseOrderStartingNumber,
            numbering.PurchaseOrder);
        AppendSeries(
            rows,
            SettingKeys.NumberingShiftPrefix,
            SettingKeys.NumberingShiftPattern,
            SettingKeys.NumberingShiftStartingNumber,
            numbering.Shift);
    }

    private static void AppendSeries(
        List<SettingRow> rows,
        string prefixKey,
        string patternKey,
        string startKey,
        DocumentNumbering series)
    {
        rows.Add(Text(prefixKey, series.Prefix));
        rows.Add(Text(patternKey, series.Pattern));
        rows.Add(Scaled(startKey, series.StartingNumber));
    }

    private static NumberingSettings ReadNumbering(
        IReadOnlyDictionary<string, StoredSetting> rows,
        NumberingSettings fallback) => new(
            ReadSeries(
                rows,
                SettingKeys.NumberingBillPrefix,
                SettingKeys.NumberingBillPattern,
                SettingKeys.NumberingBillStartingNumber,
                fallback.Bill),
            ReadSeries(
                rows,
                SettingKeys.NumberingReturnPrefix,
                SettingKeys.NumberingReturnPattern,
                SettingKeys.NumberingReturnStartingNumber,
                fallback.Return),
            ReadSeries(
                rows,
                SettingKeys.NumberingCreditNotePrefix,
                SettingKeys.NumberingCreditNotePattern,
                SettingKeys.NumberingCreditNoteStartingNumber,
                fallback.CreditNote),
            ReadSeries(
                rows,
                SettingKeys.NumberingGoodsReceiptPrefix,
                SettingKeys.NumberingGoodsReceiptPattern,
                SettingKeys.NumberingGoodsReceiptStartingNumber,
                fallback.GoodsReceipt),
            ReadSeries(
                rows,
                SettingKeys.NumberingPurchaseOrderPrefix,
                SettingKeys.NumberingPurchaseOrderPattern,
                SettingKeys.NumberingPurchaseOrderStartingNumber,
                fallback.PurchaseOrder),
            ReadSeries(
                rows,
                SettingKeys.NumberingShiftPrefix,
                SettingKeys.NumberingShiftPattern,
                SettingKeys.NumberingShiftStartingNumber,
                fallback.Shift));

    private static DocumentNumbering ReadSeries(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string prefixKey,
        string patternKey,
        string startKey,
        DocumentNumbering fallback) => new(
            ReadText(rows, prefixKey, fallback.Prefix),
            ReadText(rows, patternKey, fallback.Pattern),
            ReadLong(rows, startKey, fallback.StartingNumber));

    // ---- FR-10.5 Policy ----------------------------------------------------------------------

    private static void AppendPolicy(List<SettingRow> rows, PolicySettings policy)
    {
        rows.Add(Integer(SettingKeys.PolicyReturnWindowDays, policy.ReturnWindowDays));
        rows.Add(Boolean(SettingKeys.PolicyAllowUnlinkedReturns, policy.AllowUnlinkedReturns));
        rows.Add(Text(SettingKeys.PolicyDefaultRefundMethod, SettingTokens.From(policy.DefaultRefundMethod)));
        rows.Add(MoneyRow(SettingKeys.PolicyCashRefundLimit, policy.CashRefundLimit));
        rows.Add(Scaled(SettingKeys.PolicyMaxLineDiscountRate, policy.MaxLineDiscountRate.ToScaled()));
        rows.Add(Scaled(SettingKeys.PolicyMaxBillDiscountRate, policy.MaxBillDiscountRate.ToScaled()));
        rows.Add(Text(SettingKeys.PolicyNegativeStock, SettingTokens.From(policy.NegativeStock)));
        rows.Add(Scaled(SettingKeys.PolicyRestockingFeeRate, policy.RestockingFeeRate.ToScaled()));
    }

    private static PolicySettings ReadPolicy(
        IReadOnlyDictionary<string, StoredSetting> rows,
        PolicySettings fallback) => new(
            ReadInt(rows, SettingKeys.PolicyReturnWindowDays, fallback.ReturnWindowDays),
            ReadBool(rows, SettingKeys.PolicyAllowUnlinkedReturns, fallback.AllowUnlinkedReturns),
            SettingTokens.ToRefundMethod(
                ReadText(rows, SettingKeys.PolicyDefaultRefundMethod, string.Empty),
                fallback.DefaultRefundMethod),
            ReadMoney(rows, SettingKeys.PolicyCashRefundLimit, fallback.CashRefundLimit),
            ReadPercentage(rows, SettingKeys.PolicyMaxLineDiscountRate, fallback.MaxLineDiscountRate),
            ReadPercentage(rows, SettingKeys.PolicyMaxBillDiscountRate, fallback.MaxBillDiscountRate),
            SettingTokens.ToNegativeStockPolicy(
                ReadText(rows, SettingKeys.PolicyNegativeStock, string.Empty),
                fallback.NegativeStock),
            ReadPercentage(rows, SettingKeys.PolicyRestockingFeeRate, fallback.RestockingFeeRate));

    // ---- FR-10.6 Peripherals -----------------------------------------------------------------

    private static void AppendPeripherals(List<SettingRow> rows, PeripheralSettings peripherals)
    {
        rows.Add(Text(SettingKeys.PeripheralReceiptPrinterName, peripherals.ReceiptPrinterName));
        rows.Add(Integer(SettingKeys.PeripheralPaperWidthMm, peripherals.PaperWidthMm));
        rows.Add(Integer(SettingKeys.PeripheralReceiptCopies, peripherals.ReceiptCopies));
        rows.Add(Text(SettingKeys.PeripheralLabelPrinterName, peripherals.LabelPrinterName));
        rows.Add(Boolean(SettingKeys.PeripheralOpenDrawerOnCashSale, peripherals.OpenDrawerOnCashSale));
        rows.Add(Integer(SettingKeys.PeripheralDrawerKickPin, peripherals.DrawerKickPin));
        rows.Add(Text(SettingKeys.PeripheralScannerSuffix, SettingTokens.From(peripherals.ScannerSuffix)));
        rows.Add(Integer(SettingKeys.PeripheralScannerMinimumLength, peripherals.ScannerMinimumLength));
        rows.Add(Boolean(SettingKeys.PeripheralScaleEnabled, peripherals.ScaleEnabled));
        rows.Add(Text(SettingKeys.PeripheralScalePort, peripherals.ScalePort));
        rows.Add(Integer(SettingKeys.PeripheralScaleBaudRate, peripherals.ScaleBaudRate));
    }

    private static PeripheralSettings ReadPeripherals(
        IReadOnlyDictionary<string, StoredSetting> rows,
        PeripheralSettings fallback) => new(
            ReadText(rows, SettingKeys.PeripheralReceiptPrinterName, fallback.ReceiptPrinterName),
            ReadInt(rows, SettingKeys.PeripheralPaperWidthMm, fallback.PaperWidthMm),
            ReadInt(rows, SettingKeys.PeripheralReceiptCopies, fallback.ReceiptCopies),
            ReadText(rows, SettingKeys.PeripheralLabelPrinterName, fallback.LabelPrinterName),
            ReadBool(rows, SettingKeys.PeripheralOpenDrawerOnCashSale, fallback.OpenDrawerOnCashSale),
            ReadInt(rows, SettingKeys.PeripheralDrawerKickPin, fallback.DrawerKickPin),
            SettingTokens.ToScannerSuffix(
                ReadText(rows, SettingKeys.PeripheralScannerSuffix, string.Empty),
                fallback.ScannerSuffix),
            ReadInt(rows, SettingKeys.PeripheralScannerMinimumLength, fallback.ScannerMinimumLength),
            ReadBool(rows, SettingKeys.PeripheralScaleEnabled, fallback.ScaleEnabled),
            ReadText(rows, SettingKeys.PeripheralScalePort, fallback.ScalePort),
            ReadInt(rows, SettingKeys.PeripheralScaleBaudRate, fallback.ScaleBaudRate));

    // ---- FR-10.7 Backup ----------------------------------------------------------------------

    private static void AppendBackup(List<SettingRow> rows, BackupSettings backup)
    {
        rows.Add(Text(SettingKeys.BackupDailyTime, SettingTokens.From(backup.DailyBackupTime)));
        rows.Add(Boolean(SettingKeys.BackupOnShiftClose, backup.BackupOnShiftClose));
        rows.Add(Text(SettingKeys.BackupLocalPath, backup.LocalPath));
        rows.Add(Text(SettingKeys.BackupUsbPath, backup.UsbPath));
        rows.Add(Text(SettingKeys.BackupCloudTarget, SettingTokens.From(backup.CloudTarget)));
        rows.Add(Text(SettingKeys.BackupCloudAccount, backup.CloudAccount));
        rows.Add(Integer(SettingKeys.BackupRetentionDays, backup.RetentionDays));
        rows.Add(Integer(SettingKeys.BackupRetentionCopies, backup.RetentionCopies));

        // BackupSettings.PassphraseIsSet is not written. The passphrase lives in the operating
        // system's protected store and whether one exists is read from there, never from a row
        // inside the database the backup is a copy of.
    }

    private static BackupSettings ReadBackup(
        IReadOnlyDictionary<string, StoredSetting> rows,
        BackupSettings fallback) => new(
            SettingTokens.ToTimeOfDay(
                ReadText(rows, SettingKeys.BackupDailyTime, string.Empty),
                fallback.DailyBackupTime),
            ReadBool(rows, SettingKeys.BackupOnShiftClose, fallback.BackupOnShiftClose),
            ReadText(rows, SettingKeys.BackupLocalPath, fallback.LocalPath),
            ReadText(rows, SettingKeys.BackupUsbPath, fallback.UsbPath),
            SettingTokens.ToCloudTarget(
                ReadText(rows, SettingKeys.BackupCloudTarget, string.Empty),
                fallback.CloudTarget),
            ReadText(rows, SettingKeys.BackupCloudAccount, fallback.CloudAccount),
            ReadInt(rows, SettingKeys.BackupRetentionDays, fallback.RetentionDays),
            ReadInt(rows, SettingKeys.BackupRetentionCopies, fallback.RetentionCopies),
            fallback.PassphraseIsSet);

    // ---- FR-10.8 Receipt template ------------------------------------------------------------

    private static void AppendReceipt(List<SettingRow> rows, ReceiptSettings receipt)
    {
        rows.Add(Text(SettingKeys.ReceiptHeaderText, receipt.HeaderText));
        rows.Add(Text(SettingKeys.ReceiptFooterText, receipt.FooterText));
        rows.Add(Text(SettingKeys.ReceiptPolicyText, receipt.PolicyText));
        rows.Add(Boolean(SettingKeys.ReceiptShowLogo, receipt.ShowLogo));
        rows.Add(Boolean(SettingKeys.ReceiptShowBillBarcode, receipt.ShowBillBarcode));
        rows.Add(Boolean(SettingKeys.ReceiptShowCashierName, receipt.ShowCashierName));
        rows.Add(Boolean(SettingKeys.ReceiptShowCustomerName, receipt.ShowCustomerName));
        rows.Add(Boolean(SettingKeys.ReceiptShowItemAndUnitCount, receipt.ShowItemAndUnitCount));
        rows.Add(Boolean(SettingKeys.ReceiptShowTaxSummary, receipt.ShowTaxSummary));
        rows.Add(Boolean(SettingKeys.ReceiptShowTaxableValue, receipt.ShowTaxableValue));
        rows.Add(Boolean(SettingKeys.ReceiptShowTaxRegistrationNumber, receipt.ShowTaxRegistrationNumber));
    }

    private static ReceiptSettings ReadReceipt(
        IReadOnlyDictionary<string, StoredSetting> rows,
        ReceiptSettings fallback) => new(
            ReadText(rows, SettingKeys.ReceiptHeaderText, fallback.HeaderText),
            ReadText(rows, SettingKeys.ReceiptFooterText, fallback.FooterText),
            ReadText(rows, SettingKeys.ReceiptPolicyText, fallback.PolicyText),
            ReadBool(rows, SettingKeys.ReceiptShowLogo, fallback.ShowLogo),
            ReadBool(rows, SettingKeys.ReceiptShowBillBarcode, fallback.ShowBillBarcode),
            ReadBool(rows, SettingKeys.ReceiptShowCashierName, fallback.ShowCashierName),
            ReadBool(rows, SettingKeys.ReceiptShowCustomerName, fallback.ShowCustomerName),
            ReadBool(rows, SettingKeys.ReceiptShowItemAndUnitCount, fallback.ShowItemAndUnitCount),
            ReadBool(rows, SettingKeys.ReceiptShowTaxSummary, fallback.ShowTaxSummary),
            ReadBool(rows, SettingKeys.ReceiptShowTaxableValue, fallback.ShowTaxableValue),
            ReadBool(rows, SettingKeys.ReceiptShowTaxRegistrationNumber, fallback.ShowTaxRegistrationNumber));

    // ---- Row builders ------------------------------------------------------------------------

    private static SettingRow Text(string key, string value) =>
        new(key, value, SettingValueTypes.Text);

    private static SettingRow Integer(string key, int value) =>
        new(key, value.ToString(CultureInfo.InvariantCulture), SettingValueTypes.Number);

    /// <summary>An INT row holding a 64-bit value: a scaled rate, or a starting number.</summary>
    private static SettingRow Scaled(string key, long value) =>
        new(key, value.ToString(CultureInfo.InvariantCulture), SettingValueTypes.Number);

    private static SettingRow Boolean(string key, bool value) =>
        new(key, value ? "true" : "false", SettingValueTypes.Boolean);

    /// <summary>
    /// A MONEY row: the scaled 64-bit integer, amount times 10 000. Never a floating type
    /// (CLAUDE.md invariant 1).
    /// </summary>
    private static SettingRow MoneyRow(string key, Money value) =>
        new(key, value.ToScaled().ToString(CultureInfo.InvariantCulture), SettingValueTypes.Money);

    // ---- Row readers -------------------------------------------------------------------------

    private static string ReadText(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        string fallback) =>
        rows.TryGetValue(key, out var stored) ? stored.Value : fallback;

    private static int ReadInt(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        int fallback) =>
        rows.TryGetValue(key, out var stored)
        && int.TryParse(stored.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long ReadLong(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        long fallback) =>
        rows.TryGetValue(key, out var stored)
        && long.TryParse(stored.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ReadBool(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        bool fallback) =>
        rows.TryGetValue(key, out var stored) && bool.TryParse(stored.Value, out var parsed)
            ? parsed
            : fallback;

    private static Money ReadMoney(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        Money fallback) =>
        rows.TryGetValue(key, out var stored)
        && long.TryParse(stored.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scaled)
            ? Money.FromScaled(scaled)
            : fallback;

    private static Percentage ReadPercentage(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        Percentage fallback) =>
        rows.TryGetValue(key, out var stored)
        && long.TryParse(stored.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scaled)
            ? Percentage.FromScaled(scaled)
            : fallback;

    private static TaxRate ReadTaxRate(
        IReadOnlyDictionary<string, StoredSetting> rows,
        string key,
        TaxRate fallback) =>
        rows.TryGetValue(key, out var stored)
        && long.TryParse(stored.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scaled)
        && scaled >= 0
            ? TaxRate.FromScaled(scaled)
            : fallback;
}

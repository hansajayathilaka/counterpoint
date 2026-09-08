namespace Counterpoint.Application.Settings;

/// <summary>
/// Every <c>app_setting.key</c> the settings framework owns, in one place
/// (docs/01_DATA_MODEL.md §8, SRS FR-10.1-10.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>No caller outside this folder ever names a key.</b> A screen or a service asks
/// <c>settings.Financial.DecimalPlaces</c>; only <see cref="SettingsSerializer"/> turns that into
/// a row. Keys are here so that the mapping is greppable and so that renaming one is a compile
/// error rather than a silent reversion to a default.
/// </para>
/// <para>
/// The <c>security.*</c> keys are not here. They belong to P1-T02's
/// <c>SecurityPolicyRecorder</c>, which writes them as a record of what the shop's password
/// hashes were made with. This framework never reads, writes or deletes a key it does not own.
/// </para>
/// </remarks>
public static class SettingKeys
{
    // ---- FR-10.1 Shop profile ------------------------------------------------------------
    public const string ShopName = "shop.name";
    public const string ShopAddressLine1 = "shop.address_line1";
    public const string ShopAddressLine2 = "shop.address_line2";
    public const string ShopPhone = "shop.phone";
    public const string ShopEmail = "shop.email";
    public const string ShopTaxRegistrationNumber = "shop.tax_registration_number";
    public const string ShopLogoPath = "shop.logo_path";

    // ---- FR-10.2 Financial ---------------------------------------------------------------
    public const string FinancialCurrencyCode = "financial.currency_code";
    public const string FinancialCurrencySymbol = "financial.currency_symbol";
    public const string FinancialCurrencySymbolPosition = "financial.currency_symbol_position";
    public const string FinancialDecimalPlaces = "financial.decimal_places";
    public const string FinancialRoundingRule = "financial.rounding_rule";
    public const string FinancialQuantityDecimalPlaces = "financial.quantity_decimal_places";

    // ---- FR-10.3 Tax ---------------------------------------------------------------------
    public const string TaxPricesIncludeTax = "tax.prices_include_tax";
    public const string TaxDefaultClassName = "tax.default_class_name";
    public const string TaxDefaultRate = "tax.default_rate";
    public const string TaxLabel = "tax.label";

    // ---- FR-10.4 Numbering ---------------------------------------------------------------
    public const string NumberingBillPrefix = "numbering.bill.prefix";
    public const string NumberingBillPattern = "numbering.bill.pattern";
    public const string NumberingBillStartingNumber = "numbering.bill.starting_number";
    public const string NumberingReturnPrefix = "numbering.return.prefix";
    public const string NumberingReturnPattern = "numbering.return.pattern";
    public const string NumberingReturnStartingNumber = "numbering.return.starting_number";
    public const string NumberingCreditNotePrefix = "numbering.credit_note.prefix";
    public const string NumberingCreditNotePattern = "numbering.credit_note.pattern";
    public const string NumberingCreditNoteStartingNumber = "numbering.credit_note.starting_number";
    public const string NumberingGoodsReceiptPrefix = "numbering.goods_receipt.prefix";
    public const string NumberingGoodsReceiptPattern = "numbering.goods_receipt.pattern";
    public const string NumberingGoodsReceiptStartingNumber = "numbering.goods_receipt.starting_number";
    public const string NumberingPurchaseOrderPrefix = "numbering.purchase_order.prefix";
    public const string NumberingPurchaseOrderPattern = "numbering.purchase_order.pattern";
    public const string NumberingPurchaseOrderStartingNumber = "numbering.purchase_order.starting_number";
    public const string NumberingShiftPrefix = "numbering.shift.prefix";
    public const string NumberingShiftPattern = "numbering.shift.pattern";
    public const string NumberingShiftStartingNumber = "numbering.shift.starting_number";

    // ---- FR-10.5 Policy ------------------------------------------------------------------
    public const string PolicyReturnWindowDays = "policy.return_window_days";
    public const string PolicyAllowUnlinkedReturns = "policy.allow_unlinked_returns";
    public const string PolicyDefaultRefundMethod = "policy.default_refund_method";
    public const string PolicyCashRefundLimit = "policy.cash_refund_limit";
    public const string PolicyMaxLineDiscountRate = "policy.max_line_discount_rate";
    public const string PolicyMaxBillDiscountRate = "policy.max_bill_discount_rate";
    public const string PolicyNegativeStock = "policy.negative_stock";
    public const string PolicyRestockingFeeRate = "policy.restocking_fee_rate";

    // ---- FR-10.6 Peripherals -------------------------------------------------------------
    public const string PeripheralReceiptPrinterName = "peripheral.receipt_printer_name";
    public const string PeripheralPaperWidthMm = "peripheral.paper_width_mm";
    public const string PeripheralReceiptCopies = "peripheral.receipt_copies";
    public const string PeripheralLabelPrinterName = "peripheral.label_printer_name";
    public const string PeripheralOpenDrawerOnCashSale = "peripheral.open_drawer_on_cash_sale";
    public const string PeripheralDrawerKickPin = "peripheral.drawer_kick_pin";
    public const string PeripheralScannerSuffix = "peripheral.scanner_suffix";
    public const string PeripheralScannerMinimumLength = "peripheral.scanner_minimum_length";
    public const string PeripheralScaleEnabled = "peripheral.scale_enabled";
    public const string PeripheralScalePort = "peripheral.scale_port";
    public const string PeripheralScaleBaudRate = "peripheral.scale_baud_rate";

    // ---- FR-10.7 Backup ------------------------------------------------------------------
    public const string BackupDailyTime = "backup.daily_time";
    public const string BackupOnShiftClose = "backup.on_shift_close";
    public const string BackupLocalPath = "backup.local_path";
    public const string BackupUsbPath = "backup.usb_path";
    public const string BackupCloudTarget = "backup.cloud_target";
    public const string BackupCloudAccount = "backup.cloud_account";
    public const string BackupRetentionDays = "backup.retention_days";
    public const string BackupRetentionCopies = "backup.retention_copies";

    // ---- FR-10.8 Receipt template --------------------------------------------------------
    public const string ReceiptHeaderText = "receipt.header_text";
    public const string ReceiptFooterText = "receipt.footer_text";
    public const string ReceiptPolicyText = "receipt.policy_text";
    public const string ReceiptShowLogo = "receipt.show_logo";
    public const string ReceiptShowBillBarcode = "receipt.show_bill_barcode";
    public const string ReceiptShowCashierName = "receipt.show_cashier_name";
    public const string ReceiptShowCustomerName = "receipt.show_customer_name";
    public const string ReceiptShowItemAndUnitCount = "receipt.show_item_and_unit_count";
    public const string ReceiptShowTaxSummary = "receipt.show_tax_summary";
    public const string ReceiptShowTaxableValue = "receipt.show_taxable_value";
    public const string ReceiptShowTaxRegistrationNumber = "receipt.show_tax_registration_number";

    /// <summary>
    /// When the first-run wizard finished, ISO-8601. Absent on a database that has never been
    /// set up, which is exactly how <c>IFirstRunSetup.IsRequiredAsync</c> knows.
    /// </summary>
    public const string SetupCompletedAt = "setup.completed_at";
}

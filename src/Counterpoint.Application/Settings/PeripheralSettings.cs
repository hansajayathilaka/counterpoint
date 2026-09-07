namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.6 - the hardware on the counter. Every one of these is set on site in the
/// hardware-integration track (<c>docs/09_HARDWARE_INTEGRATION.md</c>); the defaults here are
/// what the Linux fakes need to run (CLAUDE.md "Hardware boundary").
/// </summary>
/// <param name="ReceiptPrinterName">
/// Windows printer queue name for the receipt printer. Empty means "the development file
/// printer", which is what CI and a Linux dev host use.
/// </param>
/// <param name="PaperWidthMm">Receipt paper width: 80 or 58.</param>
/// <param name="ReceiptCopies">How many copies of a bill print by default.</param>
/// <param name="LabelPrinterName">Windows printer queue name for the label printer (P1-T12).</param>
/// <param name="OpenDrawerOnCashSale">Whether a cash tender kicks the drawer open (FR-7.7).</param>
/// <param name="DrawerKickPin">
/// Which pin of the printer's kick-out port the drawer is wired to: 2 (the common wiring) or 5.
/// An integer and not the Devices enum, because the Application layer may not reference Devices.
/// </param>
/// <param name="ScannerSuffix">What the scanner sends at the end of a scan.</param>
/// <param name="ScannerMinimumLength">
/// Shortest input the till treats as a scan rather than as typing.
/// </param>
/// <param name="ScaleEnabled">Whether a weighing scale is connected at all (P5-T05).</param>
/// <param name="ScalePort">Serial port the scale is on, for example <c>COM3</c>.</param>
/// <param name="ScaleBaudRate">Serial speed of the scale.</param>
public sealed record PeripheralSettings(
    string ReceiptPrinterName,
    int PaperWidthMm,
    int ReceiptCopies,
    string LabelPrinterName,
    bool OpenDrawerOnCashSale,
    int DrawerKickPin,
    ScannerSuffix ScannerSuffix,
    int ScannerMinimumLength,
    bool ScaleEnabled,
    string ScalePort,
    int ScaleBaudRate);

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.6 - the hardware on the counter: printer, paper, drawer, scanner and scale.
/// </summary>
/// <remarks>
/// <para>
/// <b>The printer is named, not discovered.</b> Listing the machine's print queues is a Windows
/// call that belongs behind an interface in <c>Counterpoint.Devices</c>, which this project may
/// not reference, and the real queue names are read off the terminal on site in HW-T01. So the
/// box is a name the owner types, and an empty one means the development file printer - which is
/// exactly what CI and a Linux dev host use (CLAUDE.md "Hardware boundary").
/// </para>
/// <para>
/// Every value on this tab is set on site in the hardware-integration track. Nothing here talks
/// to a device; it writes a setting that a device adapter reads later.
/// </para>
/// </remarks>
public sealed partial class PeripheralSettingsViewModel : SettingsGroupViewModel
{
    private readonly EnumChoices<ScannerSuffix> _suffixes = new(
        (ScannerSuffix.Enter, "Enter - the usual factory setting"),
        (ScannerSuffix.Tab, "Tab"),
        (ScannerSuffix.None, "Nothing - decide from the typing speed"));

    private string _paperWidthMm = string.Empty;
    private string _receiptCopies = string.Empty;
    private string _drawerKickPin = string.Empty;
    private string _scannerMinimumLength = string.Empty;
    private string _scaleBaudRate = string.Empty;

    [ObservableProperty]
    private string _receiptPrinterName = string.Empty;

    [ObservableProperty]
    private string _labelPrinterName = string.Empty;

    [ObservableProperty]
    private bool _openDrawerOnCashSale;

    [ObservableProperty]
    private string _scannerSuffixChoice = string.Empty;

    [ObservableProperty]
    private bool _scaleEnabled;

    [ObservableProperty]
    private string _scalePort = string.Empty;

    /// <inheritdoc />
    public override string Title => "Peripherals";

    /// <inheritdoc />
    public override string Requirement => "FR-10.6";

    /// <summary>What the scanner sends at the end of a scan.</summary>
    public IReadOnlyList<string> ScannerSuffixChoices => _suffixes.Labels;

    /// <summary>Receipt paper width in millimetres: 80 or 58.</summary>
    public string PaperWidthMm
    {
        get => _paperWidthMm;
        set => SetNumeric(ref _paperWidthMm, value);
    }

    /// <summary>How many copies of a bill print by default.</summary>
    public string ReceiptCopies
    {
        get => _receiptCopies;
        set => SetNumeric(ref _receiptCopies, value);
    }

    /// <summary>Which pin of the printer's kick-out port the drawer is wired to.</summary>
    public string DrawerKickPin
    {
        get => _drawerKickPin;
        set => SetNumeric(ref _drawerKickPin, value);
    }

    /// <summary>Shortest input the till treats as a scan rather than as typing.</summary>
    public string ScannerMinimumLength
    {
        get => _scannerMinimumLength;
        set => SetNumeric(ref _scannerMinimumLength, value);
    }

    /// <summary>Serial speed of the scale.</summary>
    public string ScaleBaudRate
    {
        get => _scaleBaudRate;
        set => SetNumeric(ref _scaleBaudRate, value);
    }

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ReceiptPrinterName = snapshot.Peripherals.ReceiptPrinterName;
        PaperWidthMm = SettingsText.FromInt(snapshot.Peripherals.PaperWidthMm);
        ReceiptCopies = SettingsText.FromInt(snapshot.Peripherals.ReceiptCopies);
        LabelPrinterName = snapshot.Peripherals.LabelPrinterName;
        OpenDrawerOnCashSale = snapshot.Peripherals.OpenDrawerOnCashSale;
        DrawerKickPin = SettingsText.FromInt(snapshot.Peripherals.DrawerKickPin);
        ScannerSuffixChoice = _suffixes.Label(snapshot.Peripherals.ScannerSuffix);
        ScannerMinimumLength = SettingsText.FromInt(snapshot.Peripherals.ScannerMinimumLength);
        ScaleEnabled = snapshot.Peripherals.ScaleEnabled;
        ScalePort = snapshot.Peripherals.ScalePort;
        ScaleBaudRate = SettingsText.FromInt(snapshot.Peripherals.ScaleBaudRate);
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Peripherals = new PeripheralSettings(
                ReceiptPrinterName.Trim(),
                SettingsText.ToInt(PaperWidthMm, snapshot.Peripherals.PaperWidthMm),
                SettingsText.ToInt(ReceiptCopies, snapshot.Peripherals.ReceiptCopies),
                LabelPrinterName.Trim(),
                OpenDrawerOnCashSale,
                SettingsText.ToInt(DrawerKickPin, snapshot.Peripherals.DrawerKickPin),
                _suffixes.Value(ScannerSuffixChoice),
                SettingsText.ToInt(ScannerMinimumLength, snapshot.Peripherals.ScannerMinimumLength),
                ScaleEnabled,
                ScalePort.Trim(),
                SettingsText.ToInt(ScaleBaudRate, snapshot.Peripherals.ScaleBaudRate)),
        };
    }
}

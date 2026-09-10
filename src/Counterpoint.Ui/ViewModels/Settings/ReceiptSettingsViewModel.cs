using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.8 - the receipt template: the words the shop puts on a bill and which optional fields
/// print.
/// </summary>
/// <remarks>
/// The renderer wraps the policy text to the paper width, so it is typed as one paragraph and
/// not as pre-broken lines (PRT-02). Which tax fields print is here rather than on the tax tab
/// because it is a decision about the bill, and the regime stays a data decision (Q-02).
/// </remarks>
public sealed partial class ReceiptSettingsViewModel : SettingsGroupViewModel
{
    private readonly IReceiptTemplatePreviewService? _preview;

    /// <summary>For the XAML previewer, which resolves nothing from a container.</summary>
    public ReceiptSettingsViewModel()
    {
    }

    /// <param name="preview">
    /// Renders a candidate template to screen without printing (P1-T11's "Template preview in
    /// settings that renders to screen without printing"). Null only for the XAML previewer.
    /// </param>
    public ReceiptSettingsViewModel(IReceiptTemplatePreviewService preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        _preview = preview;
    }

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private string _policyText = string.Empty;

    [ObservableProperty]
    private bool _showLogo;

    [ObservableProperty]
    private bool _showBillBarcode;

    [ObservableProperty]
    private bool _showCashierName;

    [ObservableProperty]
    private bool _showCustomerName;

    [ObservableProperty]
    private bool _showItemAndUnitCount;

    [ObservableProperty]
    private bool _showTaxSummary;

    [ObservableProperty]
    private bool _showTaxableValue;

    [ObservableProperty]
    private bool _showTaxRegistrationNumber;

    [ObservableProperty]
    private string _templateText = string.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>
    /// Renders <see cref="TemplateText"/> to screen against the §10.1 specimen bill - never to
    /// the printer, never through the print outbox (P1-T11).
    /// </summary>
    [RelayCommand]
    public void Preview()
    {
        if (_preview is null)
        {
            return;
        }

        PreviewText = string.Join(
            Environment.NewLine,
            _preview.Preview(TemplateText));
    }

    /// <inheritdoc />
    public override string Title => "Receipt";

    /// <inheritdoc />
    public override string Requirement => "FR-10.8";

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        HeaderText = snapshot.Receipt.HeaderText;
        FooterText = snapshot.Receipt.FooterText;
        PolicyText = snapshot.Receipt.PolicyText;
        ShowLogo = snapshot.Receipt.ShowLogo;
        ShowBillBarcode = snapshot.Receipt.ShowBillBarcode;
        ShowCashierName = snapshot.Receipt.ShowCashierName;
        ShowCustomerName = snapshot.Receipt.ShowCustomerName;
        ShowItemAndUnitCount = snapshot.Receipt.ShowItemAndUnitCount;
        ShowTaxSummary = snapshot.Receipt.ShowTaxSummary;
        ShowTaxableValue = snapshot.Receipt.ShowTaxableValue;
        ShowTaxRegistrationNumber = snapshot.Receipt.ShowTaxRegistrationNumber;
        TemplateText = snapshot.Receipt.TemplateText;
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Receipt = new ReceiptSettings(
                HeaderText.Trim(),
                FooterText.Trim(),
                PolicyText.Trim(),
                ShowLogo,
                ShowBillBarcode,
                ShowCashierName,
                ShowCustomerName,
                ShowItemAndUnitCount,
                ShowTaxSummary,
                ShowTaxableValue,
                ShowTaxRegistrationNumber,

                // Blank means "use the shipped default" (ReceiptTemplateDefaults.SalesBillTemplate)
                // - an owner clearing the box reverts to the specimen rather than saving nothing.
                string.IsNullOrWhiteSpace(TemplateText) ? ReceiptTemplateDefaults.SalesBillTemplate : TemplateText),
        };
    }
}

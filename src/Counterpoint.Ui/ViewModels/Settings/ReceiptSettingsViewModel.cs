using System;
using CommunityToolkit.Mvvm.ComponentModel;
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
                ShowTaxRegistrationNumber),
        };
    }
}

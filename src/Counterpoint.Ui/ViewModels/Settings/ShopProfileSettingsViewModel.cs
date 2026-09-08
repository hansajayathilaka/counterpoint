using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>FR-10.1 - who the shop is, as it appears at the top of every bill and report.</summary>
public sealed partial class ShopProfileSettingsViewModel : SettingsGroupViewModel
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _addressLine1 = string.Empty;

    [ObservableProperty]
    private string _addressLine2 = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _taxRegistrationNumber = string.Empty;

    [ObservableProperty]
    private string _logoPath = string.Empty;

    /// <inheritdoc />
    public override string Title => "Shop profile";

    /// <inheritdoc />
    public override string Requirement => "FR-10.1";

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Name = snapshot.Shop.Name;
        AddressLine1 = snapshot.Shop.AddressLine1;
        AddressLine2 = snapshot.Shop.AddressLine2;
        Phone = snapshot.Shop.Phone;
        Email = snapshot.Shop.Email;
        TaxRegistrationNumber = snapshot.Shop.TaxRegistrationNumber;
        LogoPath = snapshot.Shop.LogoPath;
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Shop = new ShopProfileSettings(
                Name.Trim(),
                AddressLine1.Trim(),
                AddressLine2.Trim(),
                Phone.Trim(),
                Email.Trim(),
                TaxRegistrationNumber.Trim(),
                LogoPath.Trim()),
        };
    }
}

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.3 - how the shop charges tax: the default class, its rate, the word tax is printed
/// under, and whether a catalogue price already contains its tax.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rate is typed as a percent</b> - <c>15</c> means 15% - and becomes a
/// <c>TaxRate</c> on the way out. No rate is written anywhere in this file; the shop's regime is
/// a data decision taken at first run (Q-02).
/// </para>
/// <para>
/// <b>The list of tax classes is not editable here yet.</b> This tab edits the default class the
/// first run created and the rate it carries, which is what <c>TaxSettings</c> holds. Creating,
/// renaming and deactivating further <c>tax_class</c> rows is P1-T04's reference-data screen -
/// there is no CRUD service for them yet, and inventing one here would be building ahead.
/// </para>
/// </remarks>
public sealed partial class TaxSettingsViewModel : SettingsGroupViewModel
{
    private string _defaultTaxRate = string.Empty;

    [ObservableProperty]
    private bool _pricesIncludeTax;

    [ObservableProperty]
    private string _defaultTaxClassName = string.Empty;

    [ObservableProperty]
    private string _taxLabel = string.Empty;

    /// <inheritdoc />
    public override string Title => "Tax";

    /// <inheritdoc />
    public override string Requirement => "FR-10.3";

    /// <summary>The default class's rate, in percent.</summary>
    public string DefaultTaxRate
    {
        get => _defaultTaxRate;
        set => SetNumeric(ref _defaultTaxRate, value, allowDecimal: true);
    }

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PricesIncludeTax = snapshot.Tax.PricesIncludeTax;
        DefaultTaxClassName = snapshot.Tax.DefaultTaxClassName;
        DefaultTaxRate = SettingsText.FromTaxRate(snapshot.Tax.DefaultTaxRate);
        TaxLabel = snapshot.Tax.TaxLabel;
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Tax = new TaxSettings(
                PricesIncludeTax,
                DefaultTaxClassName.Trim(),
                SettingsText.ToTaxRate(DefaultTaxRate),
                TaxLabel.Trim()),
        };
    }
}

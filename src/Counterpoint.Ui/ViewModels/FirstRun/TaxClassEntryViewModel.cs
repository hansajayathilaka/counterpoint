using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings.FirstRun;

namespace Counterpoint.Ui.ViewModels.FirstRun;

/// <summary>
/// One tax class as the first-run wizard collects it: a name the shop uses and a rate in percent
/// (SRS FR-10.3).
/// </summary>
/// <remarks>
/// Zero is a perfectly good rate, and is the default. The regime is a data decision taken here,
/// never a code decision (Q-02) - no rate is written anywhere in this file.
/// </remarks>
public sealed partial class TaxClassEntryViewModel : NumericInputViewModel
{
    private string _rate = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    public TaxClassEntryViewModel()
    {
    }

    public TaxClassEntryViewModel(string name, string ratePercent)
    {
        Name = name;
        Rate = ratePercent;
    }

    /// <summary>The class's rate, in percent.</summary>
    public string Rate
    {
        get => _rate;
        set => SetNumeric(ref _rate, value, allowDecimal: true);
    }

    /// <summary>True when this row names a class worth creating.</summary>
    public bool IsComplete => Name.Trim().Length > 0;

    /// <summary>The row as the setup service wants it.</summary>
    public TaxClassDefinition ToDefinition() => new(Name.Trim(), SettingsText.ToTaxRate(Rate));
}

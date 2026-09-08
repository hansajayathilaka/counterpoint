using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.2 - the currency, how it is written, and how amounts are rounded.
/// </summary>
/// <remarks>
/// Changing the decimal places here changes what the till displays and what the printer prints,
/// the moment it saves and without a restart: <c>SettingsRoundingPolicy</c> reads this group on
/// every use. Nothing in this file names a currency - the shop's own is a setting, and
/// <c>NoHardCodedBusinessValuesTests</c> fails the build if one appears in the source.
/// </remarks>
public sealed partial class FinancialSettingsViewModel : SettingsGroupViewModel
{
    private readonly EnumChoices<CurrencySymbolPosition> _positions = new(
        (CurrencySymbolPosition.Before, "Before the amount"),
        (CurrencySymbolPosition.After, "After the amount"));

    private readonly EnumChoices<RoundingRule> _rules = new(
        (RoundingRule.HalfAwayFromZero, "Half up - a half rounds away from zero"),
        (RoundingRule.HalfToEven, "Banker's rounding - a half rounds to the even digit"));

    private string _decimalPlaces = string.Empty;
    private string _quantityDecimalPlaces = string.Empty;

    [ObservableProperty]
    private string _currencyCode = string.Empty;

    [ObservableProperty]
    private string _currencySymbol = string.Empty;

    [ObservableProperty]
    private string _symbolPositionChoice = string.Empty;

    [ObservableProperty]
    private string _roundingRuleChoice = string.Empty;

    /// <inheritdoc />
    public override string Title => "Financial";

    /// <inheritdoc />
    public override string Requirement => "FR-10.2";

    /// <summary>Where the symbol sits against the amount.</summary>
    public IReadOnlyList<string> SymbolPositionChoices => _positions.Labels;

    /// <summary>Which way a midpoint goes at the line total and the bill total.</summary>
    public IReadOnlyList<string> RoundingRuleChoices => _rules.Labels;

    /// <summary>The currency's minor digits.</summary>
    public string DecimalPlaces
    {
        get => _decimalPlaces;
        set => SetNumeric(ref _decimalPlaces, value);
    }

    /// <summary>How many decimals a quantity is shown to.</summary>
    public string QuantityDecimalPlaces
    {
        get => _quantityDecimalPlaces;
        set => SetNumeric(ref _quantityDecimalPlaces, value);
    }

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CurrencyCode = snapshot.Financial.CurrencyCode;
        CurrencySymbol = snapshot.Financial.CurrencySymbol;
        SymbolPositionChoice = _positions.Label(snapshot.Financial.SymbolPosition);
        DecimalPlaces = SettingsText.FromInt(snapshot.Financial.DecimalPlaces);
        RoundingRuleChoice = _rules.Label(snapshot.Financial.RoundingRule);
        QuantityDecimalPlaces = SettingsText.FromInt(snapshot.Financial.QuantityDecimalPlaces);
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Financial = new FinancialSettings(
                CurrencyCode.Trim(),
                CurrencySymbol.Trim(),
                _positions.Value(SymbolPositionChoice),

                // An empty box is not a decision. It keeps what the shop is trading on until
                // somebody types something that is a number.
                SettingsText.ToInt(DecimalPlaces, snapshot.Financial.DecimalPlaces),
                _rules.Value(RoundingRuleChoice),
                SettingsText.ToInt(QuantityDecimalPlaces, snapshot.Financial.QuantityDecimalPlaces)),
        };
    }
}

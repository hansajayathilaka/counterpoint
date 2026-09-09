using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.5 - the trading rules every later feature reads its limits from: returns, refunds,
/// discount ceilings and what happens when stock runs out.
/// </summary>
/// <remarks>
/// <para>
/// The cash refund limit is a <c>Money</c> and the three rates are <c>Percentage</c>s, built from
/// what was typed through <see cref="SettingsText"/>. No amount becomes a <c>double</c> on the
/// way (CLAUDE.md invariant 1) and nothing here rounds (invariant 2).
/// </para>
/// <para>
/// Discounts are typed as a percent: <c>100</c> is no restriction, which is the shop's answer to
/// Q-12. A number outside 0-100 is refused by <c>SettingsValidation</c> in the Application layer,
/// in a sentence the owner reads - this screen does not second-guess it.
/// </para>
/// </remarks>
public sealed partial class PolicySettingsViewModel : SettingsGroupViewModel
{
    private readonly EnumChoices<RefundMethod> _refundMethods = new(
        (RefundMethod.Cash, "Cash from the drawer"),
        (RefundMethod.CreditNote, "A credit note"),
        (RefundMethod.Card, "Back to the card"));

    private readonly EnumChoices<NegativeStockPolicy> _negativeStockPolicies = new(
        (NegativeStockPolicy.Allow, "Sell it anyway"),
        (NegativeStockPolicy.Warn, "Sell it, but warn the cashier"),
        (NegativeStockPolicy.Block, "Refuse the line"));

    private string _returnWindowDays = string.Empty;
    private string _cashRefundLimit = string.Empty;
    private string _maxLineDiscountRate = string.Empty;
    private string _maxBillDiscountRate = string.Empty;
    private string _restockingFeeRate = string.Empty;

    [ObservableProperty]
    private bool _allowUnlinkedReturns;

    [ObservableProperty]
    private string _defaultRefundMethodChoice = string.Empty;

    [ObservableProperty]
    private string _negativeStockChoice = string.Empty;

    [ObservableProperty]
    private bool _combineRepeatScans;

    /// <inheritdoc />
    public override string Title => "Policy";

    /// <inheritdoc />
    public override string Requirement => "FR-10.5";

    /// <summary>What a refund is paid out as unless the cashier changes it.</summary>
    public IReadOnlyList<string> DefaultRefundMethodChoices => _refundMethods.Labels;

    /// <summary>What a sale does when the balance would go below zero.</summary>
    public IReadOnlyList<string> NegativeStockChoices => _negativeStockPolicies.Labels;

    /// <summary>How many days after a sale a return is accepted.</summary>
    public string ReturnWindowDays
    {
        get => _returnWindowDays;
        set => SetNumeric(ref _returnWindowDays, value);
    }

    /// <summary>The most that may be refunded in cash on one return. Zero means no limit.</summary>
    public string CashRefundLimit
    {
        get => _cashRefundLimit;
        set => SetNumeric(ref _cashRefundLimit, value, allowDecimal: true);
    }

    /// <summary>The cashier's ceiling on one line's discount, in percent.</summary>
    public string MaxLineDiscountRate
    {
        get => _maxLineDiscountRate;
        set => SetNumeric(ref _maxLineDiscountRate, value, allowDecimal: true);
    }

    /// <summary>The same ceiling for a discount on the whole bill, in percent.</summary>
    public string MaxBillDiscountRate
    {
        get => _maxBillDiscountRate;
        set => SetNumeric(ref _maxBillDiscountRate, value, allowDecimal: true);
    }

    /// <summary>Proportion of the refund the shop keeps on a return, in percent.</summary>
    public string RestockingFeeRate
    {
        get => _restockingFeeRate;
        set => SetNumeric(ref _restockingFeeRate, value, allowDecimal: true);
    }

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ReturnWindowDays = SettingsText.FromInt(snapshot.Policy.ReturnWindowDays);
        AllowUnlinkedReturns = snapshot.Policy.AllowUnlinkedReturns;
        DefaultRefundMethodChoice = _refundMethods.Label(snapshot.Policy.DefaultRefundMethod);
        CashRefundLimit = SettingsText.FromMoney(snapshot.Policy.CashRefundLimit);
        MaxLineDiscountRate = SettingsText.FromPercentage(snapshot.Policy.MaxLineDiscountRate);
        MaxBillDiscountRate = SettingsText.FromPercentage(snapshot.Policy.MaxBillDiscountRate);
        NegativeStockChoice = _negativeStockPolicies.Label(snapshot.Policy.NegativeStock);
        RestockingFeeRate = SettingsText.FromPercentage(snapshot.Policy.RestockingFeeRate);
        CombineRepeatScans = snapshot.Policy.CombineRepeatScans;
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Policy = new PolicySettings(
                SettingsText.ToInt(ReturnWindowDays, snapshot.Policy.ReturnWindowDays),
                AllowUnlinkedReturns,
                _refundMethods.Value(DefaultRefundMethodChoice),
                SettingsText.ToMoney(CashRefundLimit),
                SettingsText.ToPercentage(MaxLineDiscountRate),
                SettingsText.ToPercentage(MaxBillDiscountRate),
                _negativeStockPolicies.Value(NegativeStockChoice),
                SettingsText.ToPercentage(RestockingFeeRate),
                CombineRepeatScans),
        };
    }
}

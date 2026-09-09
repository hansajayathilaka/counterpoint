using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Sales;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// One line on the bill, as the screen shows it - and, unlike the walking skeleton's version,
/// the one place a cashier edits a line's quantity or selling unit (SRS FR-3.5, FR-2.5, FR-3.7).
/// </summary>
/// <remarks>
/// <para>
/// Every figure on it came from the Application layer's <see cref="QuotedLine"/>. The UI
/// multiplies nothing and rounds nothing (CLAUDE.md invariant 2): editing the quantity or the
/// unit does not recompute <see cref="LineTotalText"/> here, it asks <see cref="SalesViewModel"/>
/// to re-quote the whole bill through <c>IQuoteSale</c> and reads the answer back.
/// </para>
/// <para>
/// A catalogue line's price is never typed in directly here (SRS FR-3.19 is an owner-only,
/// audited override this task does not build a re-authentication dialog for). What looks like
/// editing "the price" is always one of: switching the selling unit (a different, already-priced
/// option), or applying a capped discount (F7) - both of which stay inside the Application layer's
/// pricing rules. An open item (<see cref="IsOpenItem"/>) is the one line whose price the cashier
/// typed themselves, because that is what an open item is (SRS FR-2.8).
/// </para>
/// </remarks>
public sealed partial class SaleLineViewModel : NumericInputViewModel
{
    private readonly Action<int, decimal> _onQuantityChanged;
    private readonly Action<int, long> _onUnitChanged;
    private readonly Action<int> _onRemove;
    private readonly bool _initialised;

    private string _quantityText = string.Empty;

    public SaleLineViewModel(
        int index,
        QuotedLine line,
        IReadOnlyList<ScannedItemUnitOption> unitOptions,
        Action<int, decimal> onQuantityChanged,
        Action<int, long> onUnitChanged,
        Action<int> onRemove)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(unitOptions);
        ArgumentNullException.ThrowIfNull(onQuantityChanged);
        ArgumentNullException.ThrowIfNull(onUnitChanged);
        ArgumentNullException.ThrowIfNull(onRemove);

        Index = index;
        ProductVariantId = line.ProductVariantId;
        IsOpenItem = line.IsOpenItem;
        Description = line.Description;
        _quantityText = line.Quantity.ToString("0.####", CultureInfo.InvariantCulture);
        UnitPriceText = line.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        DiscountText = line.Discount.IsZero
            ? string.Empty
            : "-" + line.Discount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        LineTotalText = line.LineTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        _onQuantityChanged = onQuantityChanged;
        _onUnitChanged = onUnitChanged;
        _onRemove = onRemove;

        // A catalogue line offers every unit the product sells in (SRS FR-2.5, FR-3.7); an open
        // item has none to switch between - the unit it was keyed in on is the only one it has.
        UnitChoices = [.. unitOptions.Select(option => option.Symbol)];
        _selectedUnitSymbol = line.UomSymbol;
        _unitIdsBySymbol = unitOptions.ToDictionary(option => option.Symbol, option => option.UomId, StringComparer.Ordinal);

        _initialised = true;
    }

    /// <summary>This line's position on the bill - what the parent's edit callbacks are keyed by.</summary>
    public int Index { get; }

    /// <summary>The variant on the line, or null for an open item.</summary>
    public long? ProductVariantId { get; }

    /// <summary>True for a manually described, manually priced line (SRS FR-2.8).</summary>
    public bool IsOpenItem { get; }

    /// <summary>The name as it will be snapshotted onto the bill.</summary>
    public string Description { get; }

    /// <summary>Every unit this line could be sold in - empty for an open item.</summary>
    public IReadOnlyList<string> UnitChoices { get; }

    private readonly Dictionary<string, long> _unitIdsBySymbol;

    /// <summary>Whether a unit switch makes sense here at all - hidden entirely for an open item.</summary>
    public bool CanSwitchUnit => !IsOpenItem && UnitChoices.Count > 1;

    /// <summary>
    /// Quantity, as typed. Accepts the keypad and the number row and quietly drops anything else
    /// (SRS UI-10) - <see cref="NumericInputViewModel.SetNumeric"/> does the dropping.
    /// </summary>
    public string QuantityText
    {
        get => _quantityText;
        set => SetNumeric(ref _quantityText, value, allowDecimal: true);
    }

    [ObservableProperty]
    private string _selectedUnitSymbol;

    /// <summary>Price per unit, as priced by the Application layer.</summary>
    public string UnitPriceText { get; }

    /// <summary>The discount taken off this line, as a negative amount, or blank when there is none.</summary>
    public string DiscountText { get; }

    /// <summary>The rounded line total, as it will be charged.</summary>
    public string LineTotalText { get; }

    /// <summary>Commits an edited quantity (SRS FR-3.5 - "edit quantity").</summary>
    [RelayCommand]
    private void CommitQuantity()
    {
        if (decimal.TryParse(QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            && quantity > 0m)
        {
            _onQuantityChanged(Index, quantity);
        }
    }

    /// <summary>Removes this line from the bill (SRS FR-3.5 - "remove a line").</summary>
    [RelayCommand]
    private void Remove() => _onRemove(Index);

    partial void OnSelectedUnitSymbolChanged(string value)
    {
        if (!_initialised || !_unitIdsBySymbol.TryGetValue(value, out var uomId))
        {
            return;
        }

        _onUnitChanged(Index, uomId);
    }
}

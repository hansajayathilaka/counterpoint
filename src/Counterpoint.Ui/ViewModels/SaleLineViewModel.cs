using System;
using System.Globalization;
using Counterpoint.Application.Sales;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// One line on the bill, as the screen shows it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a <see cref="ViewModelBase"/>: it is a row in a list with an explicit
/// template, not a page the view locator has to find a view for.
/// </para>
/// <para>
/// Every figure on it came from the Application layer's <see cref="SaleQuote"/>. The UI
/// multiplies nothing and rounds nothing (CLAUDE.md invariant 2), and there is no cost here to
/// show because the DTO it is built from has none (invariant 8).
/// </para>
/// </remarks>
public sealed class SaleLineViewModel
{
    public SaleLineViewModel(QuotedLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Description = line.Description;
        QuantityText = line.Quantity.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.UomSymbol;
        UnitPriceText = line.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        LineTotalText = line.LineTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The name as it will be snapshotted onto the bill.</summary>
    public string Description { get; }

    /// <summary>Quantity and unit.</summary>
    public string QuantityText { get; }

    /// <summary>Price per unit.</summary>
    public string UnitPriceText { get; }

    /// <summary>The rounded line total, as it will be charged.</summary>
    public string LineTotalText { get; }
}

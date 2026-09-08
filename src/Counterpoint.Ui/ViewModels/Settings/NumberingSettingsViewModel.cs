using System;
using System.Collections.Generic;
using System.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.4 - the number series for every document the shop issues.
/// </summary>
public sealed class NumberingSettingsViewModel : SettingsGroupViewModel
{
    /// <inheritdoc />
    public override string Title => "Numbering";

    /// <inheritdoc />
    public override string Requirement => "FR-10.4";

    /// <summary>Sales bills.</summary>
    public DocumentNumberingViewModel Bill { get; } = new("Bills");

    /// <summary>Returns.</summary>
    public DocumentNumberingViewModel Return { get; } = new("Returns");

    /// <summary>Credit notes.</summary>
    public DocumentNumberingViewModel CreditNote { get; } = new("Credit notes");

    /// <summary>Goods received notes.</summary>
    public DocumentNumberingViewModel GoodsReceipt { get; } = new("Goods received notes");

    /// <summary>Purchase orders.</summary>
    public DocumentNumberingViewModel PurchaseOrder { get; } = new("Purchase orders");

    /// <summary>Shifts. Not named in FR-10.4, but the till cannot open one without a series.</summary>
    public DocumentNumberingViewModel Shift { get; } = new("Shifts");

    /// <summary>Every series, in the order the screen lists them.</summary>
    public IReadOnlyList<DocumentNumberingViewModel> Series =>
        [Bill, Return, CreditNote, GoodsReceipt, PurchaseOrder, Shift];

    public NumberingSettingsViewModel()
    {
        // This screen is only ever reached after first run, so every series it shows has already
        // gone through the wizard - ConfigureAsync (not InitialiseAsync) silently ignores a
        // "Starts at" edit here (CLAUDE.md invariant 4). Disable the box rather than let it look
        // live and do nothing.
        foreach (var series in Series)
        {
            series.IsStartingNumberEditable = false;
        }
    }

    /// <inheritdoc />
    public override IEnumerable<INotifyPropertyChanged> Children => Series;

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Bill.Load(snapshot.Numbering.Bill);
        Return.Load(snapshot.Numbering.Return);
        CreditNote.Load(snapshot.Numbering.CreditNote);
        GoodsReceipt.Load(snapshot.Numbering.GoodsReceipt);
        PurchaseOrder.Load(snapshot.Numbering.PurchaseOrder);
        Shift.Load(snapshot.Numbering.Shift);
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Numbering = new NumberingSettings(
                Bill.Apply(snapshot.Numbering.Bill),
                Return.Apply(snapshot.Numbering.Return),
                CreditNote.Apply(snapshot.Numbering.CreditNote),
                GoodsReceipt.Apply(snapshot.Numbering.GoodsReceipt),
                PurchaseOrder.Apply(snapshot.Numbering.PurchaseOrder),
                Shift.Apply(snapshot.Numbering.Shift)),
        };
    }
}

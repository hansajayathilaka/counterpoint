using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The item stock card screen (task P3-T06, SRS RPT-11): every ledger movement for one item in a range with its
/// reference document and running balance, opening to closing.
/// </summary>
/// <remarks>
/// Owner-only in the Application layer (<see cref="IStockCardQuery"/>). A row whose carried balance differs from the
/// balance the ledger recorded is flagged - the card reconciling is the point of the report.
/// </remarks>
public sealed partial class StockCardViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "stock card";

    private readonly IStockCardQuery _query;

    [ObservableProperty]
    private string _skuText = string.Empty;

    [ObservableProperty]
    private string _itemText = "-";

    [ObservableProperty]
    private string _openingText = "-";

    [ObservableProperty]
    private string _totalInText = "-";

    [ObservableProperty]
    private string _totalOutText = "-";

    [ObservableProperty]
    private string _closingText = "-";

    [ObservableProperty]
    private string _reconcilesText = string.Empty;

    [ObservableProperty]
    private bool _reconcilesWarning;

    public StockCardViewModel(IStockCardQuery query, TimeProvider timeProvider, ILogger<StockCardViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        Movements = new ReportTableViewModel(
            "Movements",
            "Type a SKU or scan a barcode, then press Run.",
            new ReportColumn("When", 140),
            new ReportColumn("Type", 120),
            new ReportColumn("Document", 170),
            new ReportColumn("Quantity", 100, IsNumeric: true),
            new ReportColumn("Running balance", 130, IsNumeric: true),
            new ReportColumn("Ledger balance", 130, IsNumeric: true),
            new ReportColumn("Unit cost", 100, IsNumeric: true),
            new ReportColumn("Note", 300));
    }

    public ReportTableViewModel Movements { get; }

    /// <summary>Runs the card for the typed SKU or barcode over the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var key = SkuText.Trim();
        if (key.Length == 0)
        {
            Status = "Type a SKU or scan a barcode first.";
            return;
        }

        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var card = await _query.GetStockCardBySkuAsync(key, range, cancellationToken).ConfigureAwait(true);

                Movements.Clear();
                if (card is null)
                {
                    ItemText = "-";
                    OpeningText = TotalInText = TotalOutText = ClosingText = "-";
                    ReconcilesText = string.Empty;
                    ReconcilesWarning = false;
                    Status = "No item has that SKU or barcode.";
                    return;
                }

                var unit = card.BaseUomSymbol;
                ItemText = card.Sku + " - " + card.Description + " (" + unit + ")";
                OpeningText = ReportText.Quantity(card.OpeningBalance, unit);
                TotalInText = ReportText.Quantity(card.TotalIn, unit);
                TotalOutText = ReportText.Quantity(card.TotalOut, unit);
                ClosingText = ReportText.Quantity(card.ClosingBalance, unit);

                foreach (var row in card.Rows)
                {
                    var cells = new[]
                    {
                        ReportText.Stamp(row.OccurredAt),
                        ReportText.Token(row.MovementType),
                        row.ReferenceNo.Length > 0
                            ? row.ReferenceNo
                            : row.RefDocId is { } id ? ReportText.Token(row.RefDocType) + " #" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) : ReportText.Token(row.RefDocType),
                        ReportText.Decimal(row.QtyBase.Value),
                        ReportText.Decimal(row.RunningBalance.Value),
                        ReportText.Decimal(row.BalanceAfter.Value),
                        ReportText.Money(row.UnitCost),
                        NoteWithPostingFlags(row),
                    };

                    if (row.MatchesLedger)
                    {
                        Movements.Add(cells);
                    }
                    else
                    {
                        Movements.AddWarning([4, 5], cells);
                    }
                }

                ReconcilesWarning = !card.Reconciles;
                ReconcilesText = !card.Reconciles
                    ? "Does not reconcile: the ledger chain for this item is broken inside this range. Ask for support."
                    : card.IsContiguous
                        ? "Reconciles: opening balance plus every movement equals the closing balance, and every row matches the ledger."
                        : "Reconciles: every row follows on from the movement posted before it. Movements posted in between are dated outside "
                            + "this range, so opening plus the movements shown does not add up to the closing balance.";

                Status = card.Rows.Count == 0 ? "No movements for this item in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }

    // Says why a row's opening is not the previous day's close: it was entered after movements dated later than it
    // (a back-dated receipt or adjustment), or it follows movements that sit outside the range. Rows posted in
    // date order carry only their own note.
    private static string NoteWithPostingFlags(StockCardRow row)
    {
        var note = row.Note ?? string.Empty;
        string? flag = (row.PostedOutOfDateOrder, row.FollowsRowsNotShown) switch
        {
            (true, true) => "Back-dated: entered after later-dated movements, some outside this range.",
            (true, false) => "Back-dated: entered after later-dated movements.",
            (false, true) => "Follows movements dated outside this range.",
            _ => null,
        };

        return flag is null ? note : note.Length == 0 ? flag : note + " - " + flag;
    }
}

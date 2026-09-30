using System;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The eleven Stock, Tax and Cash report screens the back office's Reports nav group hosts beside the four sales
/// screens (task P3-T06), and which one a section name selects. A thin container: each screen owns its own state
/// and its own Application-layer query.
/// </summary>
public sealed class StockAndCashReportsViewModel : ViewModelBase
{
    /// <summary>SRS RPT-08, stock on hand (both roles).</summary>
    public const string StockOnHandSection = "Stock on hand";

    /// <summary>SRS RPT-10, low stock and reorder (both roles).</summary>
    public const string ReorderSection = "Reorder list";

    /// <summary>SRS RPT-09, stock valuation (owner).</summary>
    public const string StockValuationSection = "Stock valuation";

    /// <summary>SRS RPT-11, stock card (owner).</summary>
    public const string StockCardSection = "Stock card";

    /// <summary>SRS RPT-12, slow-moving and dead stock (owner).</summary>
    public const string SlowMovingSection = "Slow-moving stock";

    /// <summary>SRS RPT-13, fast-moving items (owner).</summary>
    public const string FastMovingSection = "Fast-moving items";

    /// <summary>SRS RPT-15, damage and shrinkage (owner).</summary>
    public const string DamageSection = "Damage and adjustments";

    /// <summary>SRS RPT-16, supplier purchases (owner).</summary>
    public const string SupplierPurchasesSection = "Supplier purchases";

    /// <summary>SRS RPT-19, tax report (owner).</summary>
    public const string TaxSection = "Tax";

    /// <summary>Tender reconciliation - not SRS RPT-05, which is the Z report (owner).</summary>
    public const string TenderReconciliationSection = "Tender reconciliation";

    /// <summary>SRS RPT-21, shift and cash-variance history (owner).</summary>
    public const string ShiftVarianceSection = "Shift variance";

    public StockAndCashReportsViewModel(
        StockOnHandViewModel stockOnHand,
        ReorderListViewModel reorder,
        StockValuationViewModel stockValuation,
        StockCardViewModel stockCard,
        SlowMovingStockViewModel slowMoving,
        FastMovingViewModel fastMoving,
        DamageAdjustmentViewModel damage,
        SupplierPurchasesViewModel supplierPurchases,
        TaxReportViewModel tax,
        TenderReconciliationViewModel tenderReconciliation,
        ShiftVarianceViewModel shiftVariance)
    {
        ArgumentNullException.ThrowIfNull(stockOnHand);
        ArgumentNullException.ThrowIfNull(reorder);
        ArgumentNullException.ThrowIfNull(stockValuation);
        ArgumentNullException.ThrowIfNull(stockCard);
        ArgumentNullException.ThrowIfNull(slowMoving);
        ArgumentNullException.ThrowIfNull(fastMoving);
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(supplierPurchases);
        ArgumentNullException.ThrowIfNull(tax);
        ArgumentNullException.ThrowIfNull(tenderReconciliation);
        ArgumentNullException.ThrowIfNull(shiftVariance);

        StockOnHand = stockOnHand;
        Reorder = reorder;
        StockValuation = stockValuation;
        StockCard = stockCard;
        SlowMoving = slowMoving;
        FastMoving = fastMoving;
        Damage = damage;
        SupplierPurchases = supplierPurchases;
        Tax = tax;
        TenderReconciliation = tenderReconciliation;
        ShiftVariance = shiftVariance;
    }

    public StockOnHandViewModel StockOnHand { get; }

    public ReorderListViewModel Reorder { get; }

    public StockValuationViewModel StockValuation { get; }

    public StockCardViewModel StockCard { get; }

    public SlowMovingStockViewModel SlowMoving { get; }

    public FastMovingViewModel FastMoving { get; }

    public DamageAdjustmentViewModel Damage { get; }

    public SupplierPurchasesViewModel SupplierPurchases { get; }

    public TaxReportViewModel Tax { get; }

    public TenderReconciliationViewModel TenderReconciliation { get; }

    public ShiftVarianceViewModel ShiftVariance { get; }

    /// <summary>
    /// Runs the screen <paramref name="section"/> names, so it opens with figures rather than an empty table -
    /// except the stock card, which needs an item typed first. Returns false for a name that is not one of these
    /// screens (so the caller can offer it to the sales screens).
    /// </summary>
    public bool Load(string section)
    {
        ArgumentNullException.ThrowIfNull(section);

        switch (section)
        {
            case StockOnHandSection:
                StockOnHand.RunCommand.Execute(null);
                return true;
            case ReorderSection:
                Reorder.RunCommand.Execute(null);
                return true;
            case StockValuationSection:
                StockValuation.RunCommand.Execute(null);
                return true;
            case StockCardSection:
                return true;
            case SlowMovingSection:
                SlowMoving.RunCommand.Execute(null);
                return true;
            case FastMovingSection:
                FastMoving.RunCommand.Execute(null);
                return true;
            case DamageSection:
                Damage.RunCommand.Execute(null);
                return true;
            case SupplierPurchasesSection:
                SupplierPurchases.RunCommand.Execute(null);
                return true;
            case TaxSection:
                Tax.RunCommand.Execute(null);
                return true;
            case TenderReconciliationSection:
                TenderReconciliation.RunCommand.Execute(null);
                return true;
            case ShiftVarianceSection:
                ShiftVariance.RunCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }
}

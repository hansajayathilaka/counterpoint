using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The tax report (task P3-T06 "Do this" #2, SRS §9 RPT-19): taxable value and tax collected by
/// rate for a period, with the tax on returns netted off - what the shop's accountant files from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-19 for the owner role), enforced here in the Application layer.
/// The rows carry no cost figure.
/// </para>
/// <para>
/// <b>Data-driven, never a regime baked in (Q-02).</b> Nothing here knows a rate. Rows are the
/// distinct <c>sale_line.tax_rate</c> values actually charged - the per-line snapshot, never the
/// current <c>tax_class</c> - and the <see cref="TaxReportHeader"/> carries the shop's own name,
/// registration number, tax label and pricing basis from settings, so the same report serves a VAT,
/// GST or sales-tax shop unchanged.
/// </para>
/// <para>
/// <b>Basis.</b> Taxable value is always <i>excluding</i> tax, in both pricing modes
/// (<see cref="TaxReportHeader.PricesIncludeTax"/> only says how the shelf prices were quoted):
/// <c>line_total</c> less the line's share of the bill discount. Sales are taken at the bill's
/// <c>business_date</c> (accrual at the point of sale), returns at their own <c>business_date</c>;
/// cancelled bills never count.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ITaxReportQuery
{
    /// <summary>The tax report for <paramref name="range"/>.</summary>
    public Task<TaxReport> GetTaxReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>What the report header says about the shop and its tax regime, read from settings.</summary>
/// <param name="ShopName">The shop's trading name (<c>shop.name</c>).</param>
/// <param name="TaxRegistrationNumber">The shop's tax registration number; empty when not set.</param>
/// <param name="TaxLabel">What tax is called in this shop (<c>tax.label</c>), for example "VAT" or "GST".</param>
/// <param name="PricesIncludeTax">True when shelf prices already contain tax (<c>tax.prices_include_tax</c>).</param>
public sealed record TaxReportHeader(
    string ShopName,
    string TaxRegistrationNumber,
    string TaxLabel,
    bool PricesIncludeTax);

/// <summary>Why a <see cref="TaxReportRow"/> exists.</summary>
public enum TaxReportRowKind
{
    /// <summary>A rate that was actually charged on a bill line (a zero rate is the exempt / non-taxable row).</summary>
    Rate,

    /// <summary>
    /// Return lines with no link to an original bill line (<c>sale_return_line.sale_line_id</c> is
    /// NULL), so the rate they were sold at is not recorded. Never guessed.
    /// </summary>
    UnlinkedReturns,
}

/// <summary>One rate's line of the tax report.</summary>
/// <param name="Kind">Whether this is a charged rate or the unlinked-returns row.</param>
/// <param name="Rate">The rate as charged on the bill line; null exactly when <paramref name="Kind"/> is <see cref="TaxReportRowKind.UnlinkedReturns"/>.</param>
/// <param name="SalesTaxable">Completed sales' taxable value at this rate, excluding tax.</param>
/// <param name="SalesTax">Tax collected on those sales at this rate.</param>
/// <param name="ReturnsTaxable">Pre-tax value returned (<c>sale_return_line.line_refund</c>) for lines originally sold at this rate.</param>
/// <param name="ReturnsTax">Tax given back on those returned lines (<c>sale_return_line.tax</c>).</param>
/// <param name="NetTaxable"><paramref name="SalesTaxable"/> minus <paramref name="ReturnsTaxable"/>.</param>
/// <param name="NetTax"><paramref name="SalesTax"/> minus <paramref name="ReturnsTax"/> - what is owed for this rate.</param>
public sealed record TaxReportRow(
    TaxReportRowKind Kind,
    TaxRate? Rate,
    Money SalesTaxable,
    Money SalesTax,
    Money ReturnsTaxable,
    Money ReturnsTax,
    Money NetTaxable,
    Money NetTax);

/// <summary>The tax report for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Header">Shop, registration number, tax label and basis.</param>
/// <param name="Rows">One row per distinct charged rate (lowest first), then the unlinked-returns row when there is one.</param>
/// <param name="TotalSalesTaxable">Sum of every row's <see cref="TaxReportRow.SalesTaxable"/>.</param>
/// <param name="TotalSalesTax">Sum of every row's <see cref="TaxReportRow.SalesTax"/>.</param>
/// <param name="TotalReturnsTaxable">Sum of every row's <see cref="TaxReportRow.ReturnsTaxable"/>.</param>
/// <param name="TotalNetTaxable">Sum of every row's <see cref="TaxReportRow.NetTaxable"/>: <see cref="TotalSalesTaxable"/> less <see cref="TotalReturnsTaxable"/>.</param>
/// <param name="TotalReturnsTax">Sum of every row's <see cref="TaxReportRow.ReturnsTax"/>.</param>
/// <param name="NetTax">Tax collected less tax given back: <see cref="TotalSalesTax"/> minus <see cref="TotalReturnsTax"/>.</param>
/// <param name="SaleLineTaxTotal"><c>SUM(sale_line.tax)</c> for the period's completed sales, read independently of the rows.</param>
/// <param name="SaleHeaderTaxTotal"><c>SUM(sale.tax)</c> for the same sales, read independently of the rows.</param>
/// <param name="ReturnHeaderTaxTotal"><c>SUM(sale_return.tax)</c> for the period's returns, read independently of the rows.</param>
/// <param name="IsReconciled">
/// True when <see cref="TotalSalesTax"/> equals both <see cref="SaleLineTaxTotal"/> and
/// <see cref="SaleHeaderTaxTotal"/>, and <see cref="TotalReturnsTax"/> equals
/// <see cref="ReturnHeaderTaxTotal"/>. False is a defect, not a rounding artefact.
/// </param>
public sealed record TaxReport(
    ReportDateRange Range,
    TaxReportHeader Header,
    IReadOnlyList<TaxReportRow> Rows,
    Money TotalSalesTaxable,
    Money TotalSalesTax,
    Money TotalReturnsTaxable,
    Money TotalNetTaxable,
    Money TotalReturnsTax,
    Money NetTax,
    Money SaleLineTaxTotal,
    Money SaleHeaderTaxTotal,
    Money ReturnHeaderTaxTotal,
    bool IsReconciled);

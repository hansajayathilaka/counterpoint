using System.Globalization;
using System.Linq;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// Builds a <see cref="ReceiptTemplateModel"/> from a <see cref="SaleReceipt"/> and the shop's
/// settings - the one place that mapping is written, shared by
/// <c>EscPosSaleReceiptRenderer</c> (a real bill) and <c>ReceiptTemplatePreviewService</c> (a
/// specimen, for the settings screen).
/// </summary>
public static class ReceiptTemplateModelBuilder
{
    /// <summary>Tender type that opens the drawer (SRS FR-7.7).</summary>
    private const string CashTender = "CASH";

    public static ReceiptTemplateModel Build(
        SaleReceipt receipt,
        SettingsSnapshot settings,
        IRoundingPolicy rounding,
        bool isDuplicate)
    {
        var cashTendered = receipt.Tenders.Any(
            tender => string.Equals(tender.TenderType, CashTender, System.StringComparison.Ordinal));

        string FormatAmount(Money amount)
        {
            var format = rounding.DecimalPlaces > 0
                ? "0." + new string('0', rounding.DecimalPlaces)
                : "0";

            return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
        }

        string FormatQuantity(SaleReceiptLine line) =>
            line.Quantity.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.UomSymbol;

        return new ReceiptTemplateModel
        {
            Shop = new ReceiptTemplateShop
            {
                Name = settings.Shop.Name,
                AddressLine1 = settings.Shop.AddressLine1,
                AddressLine2 = settings.Shop.AddressLine2,
                Phone = settings.Shop.Phone,
                TaxRegistrationNumber = settings.Shop.TaxRegistrationNumber,
            },
            Bill = new ReceiptTemplateBill
            {
                BillNo = receipt.BillNo,
                Subtotal = FormatAmount(receipt.Subtotal),
                Discount = FormatAmount(receipt.Discount),
                HasDiscount = !receipt.Discount.IsZero,
                TaxableValue = FormatAmount(receipt.TaxableValue),
                Tax = FormatAmount(receipt.Tax),
                Total = FormatAmount(receipt.Total),
                Change = FormatAmount(receipt.Change),
                HasChange = !receipt.Change.IsZero,
                HasCashTender = cashTendered,
            },
            Lines = [.. receipt.Lines.Select(line => new ReceiptTemplateLine
            {
                Description = line.Description,
                QuantityText = FormatQuantity(line),
                Rate = FormatAmount(line.UnitPrice),
                Amount = FormatAmount(line.LineTotal),
            })],
            Tenders = [.. receipt.Tenders.Select(
                tender => new ReceiptTemplateTender { TenderType = tender.TenderType, Amount = FormatAmount(tender.Amount) })],
            TaxBreakdown = [.. receipt.TaxBreakdown.Select(
                tax => new ReceiptTemplateTaxLine
                {
                    Label = tax.Label,
                    TaxableAmount = FormatAmount(tax.TaxableAmount),
                    TaxAmount = FormatAmount(tax.TaxAmount),
                })],
            PolicyText = settings.Receipt.PolicyText,
            HeaderText = settings.Receipt.HeaderText,
            FooterText = settings.Receipt.FooterText,
            CashierName = settings.Receipt.ShowCashierName ? receipt.CashierName : string.Empty,
            CustomerName = settings.Receipt.ShowCustomerName ? receipt.CustomerName : string.Empty,
            Date = receipt.SoldAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Time = receipt.SoldAt.ToString("HH:mm", CultureInfo.InvariantCulture),
            IsDuplicate = isDuplicate,
            ShowLogo = settings.Receipt.ShowLogo,
            ShowBillBarcode = settings.Receipt.ShowBillBarcode,
            ShowCashierName = settings.Receipt.ShowCashierName,
            ShowCustomerName = settings.Receipt.ShowCustomerName,
            ShowItemAndUnitCount = settings.Receipt.ShowItemAndUnitCount,
            ShowTaxSummary = settings.Receipt.ShowTaxSummary,
            ShowTaxableValue = settings.Receipt.ShowTaxableValue,
            ShowTaxRegistrationNumber = settings.Receipt.ShowTaxRegistrationNumber,
            ItemCount = receipt.Lines.Count,
            UnitsTotal = receipt.Lines
                .Sum(line => line.Quantity.Value)
                .ToString("0.####", CultureInfo.InvariantCulture),
        };
    }
}

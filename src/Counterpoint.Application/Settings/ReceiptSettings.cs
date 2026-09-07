namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.8 - the receipt template: the words the shop puts on a bill and which optional fields
/// print. P1-T11 renders a real sale through these; <c>SpecimenReceipt</c> already does.
/// </summary>
/// <param name="HeaderText">
/// A line printed under the shop's name and address, for example a slogan. Empty prints nothing.
/// </param>
/// <param name="FooterText">The parting line at the bottom of the bill.</param>
/// <param name="PolicyText">
/// The return policy paragraph, wrapped to the paper width by the renderer (PRT-02).
/// </param>
/// <param name="ShowLogo">Whether the logo bitmap prints above the header.</param>
/// <param name="ShowBillBarcode">Whether the bill number prints as a Code 128 barcode (PRT-04).</param>
/// <param name="ShowCashierName">Whether the cashier's name prints.</param>
/// <param name="ShowCustomerName">Whether the customer's name prints.</param>
/// <param name="ShowItemAndUnitCount">Whether the "Items / Units" tally prints.</param>
/// <param name="ShowTaxSummary">
/// Whether the tax lines print. One of the FR-10.3 receipt tax fields: present and editable, so
/// that the tax regime stays a data decision (Q-02).
/// </param>
/// <param name="ShowTaxableValue">Whether the taxable value prints above the tax line.</param>
/// <param name="ShowTaxRegistrationNumber">Whether the shop's tax registration number prints.</param>
public sealed record ReceiptSettings(
    string HeaderText,
    string FooterText,
    string PolicyText,
    bool ShowLogo,
    bool ShowBillBarcode,
    bool ShowCashierName,
    bool ShowCustomerName,
    bool ShowItemAndUnitCount,
    bool ShowTaxSummary,
    bool ShowTaxableValue,
    bool ShowTaxRegistrationNumber);

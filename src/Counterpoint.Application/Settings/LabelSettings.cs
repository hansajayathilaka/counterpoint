namespace Counterpoint.Application.Settings;

/// <summary>
/// SRS FR-2.10, FR-2.12 - the shelf label: its physical size and which fields print. Distinct
/// from <see cref="PeripheralSettings.LabelPrinterName"/>, which only names the printer queue
/// (FR-10.6); this group is the layout the TSPL renderer draws onto whatever label stock is
/// loaded (P1-T12).
/// </summary>
/// <param name="WidthMm">Label width, in millimetres - <c>TSPL SIZE</c>'s first argument.</param>
/// <param name="HeightMm">Label height, in millimetres - <c>TSPL SIZE</c>'s second argument.</param>
/// <param name="GapMm">
/// The gap between labels on the roll, in millimetres - <c>TSPL GAP</c>. Zero for continuous
/// (gapless) stock.
/// </param>
/// <param name="ShowProductName">Whether the product name prints.</param>
/// <param name="ShowCode">Whether the variant's code (SKU) prints as text.</param>
/// <param name="ShowBarcode">Whether the barcode symbol prints.</param>
/// <param name="ShowUnit">Whether the unit of sale prints.</param>
/// <param name="ShowPrice">Whether the retail price prints.</param>
/// <param name="DefaultQuantityPerLabel">
/// How many copies the selection screen proposes for a newly added product, before the owner
/// changes it.
/// </param>
public sealed record LabelSettings(
    int WidthMm,
    int HeightMm,
    int GapMm,
    bool ShowProductName,
    bool ShowCode,
    bool ShowBarcode,
    bool ShowUnit,
    bool ShowPrice,
    int DefaultQuantityPerLabel);

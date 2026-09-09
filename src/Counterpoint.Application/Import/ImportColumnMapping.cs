namespace Counterpoint.Application.Import;

/// <summary>
/// Which spreadsheet column feeds which product field (SRS FR-2.22). Every value is either the
/// file's own header text (matched case-insensitively) or a 0-based column index as text (for a
/// file with no usable header row); null leaves the field unmapped.
/// </summary>
/// <param name="Code"><c>product.code</c> - required.</param>
/// <param name="Name"><c>product.name</c> - required.</param>
/// <param name="NameAlt">An alternate-language name, if the shop keeps one - optional.</param>
/// <param name="Category">
/// A top-level category name. Unknown text creates a new top-level category (SRS FR-2.20's
/// two-level rule means a spreadsheet import never targets a sub-category) - optional, blank is
/// "unclassified".
/// </param>
/// <param name="Brand">A brand name. Unknown text creates a new brand - optional, blank is "unbranded".</param>
/// <param name="Unit">
/// The product's base unit, matched against an existing <c>uom.name</c> or <c>uom.symbol</c> -
/// required. Never auto-created: a unit needs <c>decimal_places</c> decided, which a catalogue
/// spreadsheet has no column for.
/// </param>
/// <param name="Type">
/// <c>STANDARD</c>/<c>DECIMAL</c>/<c>SERVICE</c>/<c>NON_INVENTORY</c> (or the friendlier
/// "Standard"/"Fractional"/"Service"/"Non-Inventory") - optional, blank defaults to STANDARD.
/// </param>
/// <param name="TaxClass">
/// An existing <c>tax_class.name</c> - required. Never auto-created: a tax class needs a rate
/// decided, which a catalogue spreadsheet has no column for.
/// </param>
/// <param name="Location">Rack or bin, free text - optional.</param>
/// <param name="NonReturnable">
/// "Y"/"Yes"/"True"/"1" for true, anything else (including blank) for false - optional.
/// </param>
/// <param name="WarrantyDays">A whole number of days - optional.</param>
/// <param name="Notes">Free text - optional.</param>
/// <param name="Barcode">
/// The variant's primary barcode - optional. A blank cell leaves the row's barcodes untouched.
/// </param>
/// <param name="Price">The variant's retail price, per base unit - required.</param>
/// <param name="Cost">
/// The unit cost the opening stock movement is posted at - optional, blank is zero.
/// </param>
/// <param name="Qty">
/// The quantity on hand <em>as of this file</em>, in the base unit - optional, blank is zero. Not
/// an increment: the importer posts the difference between this and the variant's current balance
/// as one <c>OPENING</c> movement, so re-importing a file whose figures already match the shop's
/// stock posts nothing (SRS FR-2.22's "re-import is idempotent", FR-2.23's export round trip).
/// </param>
public sealed record ImportColumnMapping(
    string? Code,
    string? Name,
    string? NameAlt,
    string? Category,
    string? Brand,
    string? Unit,
    string? Type,
    string? TaxClass,
    string? Location,
    string? NonReturnable,
    string? WarrantyDays,
    string? Notes,
    string? Barcode,
    string? Price,
    string? Cost,
    string? Qty)
{
    /// <summary>
    /// The identity mapping: every field named exactly as <see cref="CatalogueExportColumns"/>
    /// writes it, so a file this application exported can be re-imported with no mapping profile
    /// at all.
    /// </summary>
    public static ImportColumnMapping Default { get; } = new(
        Code: CatalogueExportColumns.Code,
        Name: CatalogueExportColumns.Name,
        NameAlt: CatalogueExportColumns.NameAlt,
        Category: CatalogueExportColumns.Category,
        Brand: CatalogueExportColumns.Brand,
        Unit: CatalogueExportColumns.Unit,
        Type: CatalogueExportColumns.Type,
        TaxClass: CatalogueExportColumns.TaxClass,
        Location: CatalogueExportColumns.Location,
        NonReturnable: CatalogueExportColumns.NonReturnable,
        WarrantyDays: CatalogueExportColumns.WarrantyDays,
        Notes: CatalogueExportColumns.Notes,
        Barcode: CatalogueExportColumns.Barcode,
        Price: CatalogueExportColumns.Price,
        Cost: CatalogueExportColumns.Cost,
        Qty: CatalogueExportColumns.Qty);
}

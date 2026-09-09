using System.Collections.Generic;

namespace Counterpoint.Application.Import;

/// <summary>
/// The canonical column headers <c>CatalogueImportService</c> exports the catalogue with, and the
/// mapping <see cref="ImportColumnMapping"/> a re-import needs none of if it simply hands the
/// exported file straight back (SRS FR-2.22, FR-2.23).
/// </summary>
/// <remarks>
/// One set of names, read by both directions. If the exporter and the default importer mapping
/// ever disagreed on a header, "export, then re-import" would stop round-tripping - the whole
/// point of naming them once here instead of twice, once in
/// <c>CatalogueImportService.ExportCatalogueAsync</c> and once in
/// <see cref="ImportColumnMapping.Default"/>.
/// </remarks>
public static class CatalogueExportColumns
{
    public const string Code = "Code";
    public const string Name = "Name";
    public const string NameAlt = "Alt Name";
    public const string Category = "Category";
    public const string Brand = "Brand";
    public const string Unit = "Unit";
    public const string Type = "Type";
    public const string TaxClass = "Tax Class";
    public const string Location = "Location";
    public const string NonReturnable = "Non-Returnable";
    public const string WarrantyDays = "Warranty Days";
    public const string Notes = "Notes";
    public const string Barcode = "Barcode";
    public const string Price = "Price";
    public const string Cost = "Cost";
    public const string Qty = "Qty";

    /// <summary>Every column, in the order the exporter writes them.</summary>
    public static IReadOnlyList<string> Headers { get; } =
    [
        Code, Name, NameAlt, Category, Brand, Unit, Type, TaxClass, Location,
        NonReturnable, WarrantyDays, Notes, Barcode, Price, Cost, Qty,
    ];
}

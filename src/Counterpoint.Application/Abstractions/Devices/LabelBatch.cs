using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// One product's worth of shelf labels: name, code, barcode, unit and price (SRS FR-2.10), and
/// how many copies to print of it.
/// </summary>
/// <param name="ProductName">The product name, as the label prints it.</param>
/// <param name="Code">The variant's code (SKU), printed as plain text under the barcode.</param>
/// <param name="Barcode">
/// The symbol to encode. The variant's primary barcode when it has one;
/// <c>Counterpoint.Application.Labels.LabelPrintService</c> falls back to the SKU for a variant
/// that has never had one attached, so a label is never printed with nothing to scan.
/// </param>
/// <param name="UomSymbol">The unit of sale, for example <c>kg</c> or <c>pc</c>.</param>
/// <param name="UnitPrice">Retail price per base unit.</param>
/// <param name="QuantityPerLabel">How many copies of this one label to print.</param>
public sealed record LabelBatchItem(
    string ProductName,
    string Code,
    string Barcode,
    string UomSymbol,
    Money UnitPrice,
    int QuantityPerLabel);

/// <summary>
/// A print run: a product list, a search result set, or a GRN batch (SRS FR-2.10, FR-2.12) -
/// whatever the caller selected, flattened to what the renderer needs and nothing else.
/// </summary>
public sealed record LabelBatch(IReadOnlyList<LabelBatchItem> Items);

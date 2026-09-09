using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Labels;

/// <summary>
/// Gathers what a label needs from the catalogue, renders the batch, and sends it to the label
/// printer (SRS FR-2.10, FR-2.12).
/// </summary>
/// <remarks>
/// Internal, for the same reason every other catalogue-administration service is: the role check
/// on <see cref="ILabelPrintService"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </remarks>
internal sealed class LabelPrintService : ILabelPrintService
{
    private readonly IProductStore _products;
    private readonly IProductLookup _catalogue;
    private readonly IBarcodeStore _barcodes;
    private readonly ISettings _settings;
    private readonly ILabelRenderer _renderer;
    private readonly ILabelPrinter _printer;

    public LabelPrintService(
        IProductStore products,
        IProductLookup catalogue,
        IBarcodeStore barcodes,
        ISettings settings,
        ILabelRenderer renderer,
        ILabelPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(barcodes);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(printer);

        _products = products;
        _catalogue = catalogue;
        _barcodes = barcodes;
        _settings = settings;
        _renderer = renderer;
        _printer = printer;
    }

    /// <inheritdoc />
    public async Task<PrintOutcome> PrintAsync(
        IReadOnlyList<LabelPrintRequestItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new ArgumentException(
                "At least one product is needed to print a label.",
                nameof(items));
        }

        var batchItems = new List<LabelBatchItem>(items.Count);

        foreach (var item in items)
        {
            if (item.QuantityPerLabel < 1)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Variant {item.ProductVariantId} asked for {item.QuantityPerLabel} labels; at least 1 is needed."),
                    nameof(items));
            }

            batchItems.Add(await ResolveAsync(item, cancellationToken).ConfigureAwait(false));
        }

        var document = _renderer.Render(new LabelBatch(batchItems), _settings.Current.Label);

        var jobName = string.Create(
            CultureInfo.InvariantCulture,
            $"LABELS-{batchItems.Count}");

        return await _printer.PrintAsync(document, jobName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Joins the three catalogue reads a label needs: the code (<c>product_variant.sku</c>), the
    /// name/unit/price (<see cref="IProductLookup"/>, the same source a scan at the till reads),
    /// and the barcode to encode.
    /// </summary>
    private async Task<LabelBatchItem> ResolveAsync(
        LabelPrintRequestItem item,
        CancellationToken cancellationToken)
    {
        var variant = await _products
            .FindVariantByIdAsync(item.ProductVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Product variant {item.ProductVariantId} does not exist."));

        var catalogueEntry = await _catalogue
            .FindByVariantIdAsync(item.ProductVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Product variant {item.ProductVariantId} is not sellable."));

        var barcode = await ResolveBarcodeAsync(item.ProductVariantId, variant.Sku, cancellationToken)
            .ConfigureAwait(false);

        return new LabelBatchItem(
            catalogueEntry.Description,
            variant.Sku,
            barcode,
            catalogueEntry.UomSymbol,
            catalogueEntry.UnitPrice,
            item.QuantityPerLabel);
    }

    /// <summary>
    /// The variant's primary barcode. A variant that has never had one attached falls back to its
    /// SKU, so a label is never printed with nothing under the scanner - <c>HW-T03</c> confirms
    /// the SKU itself scans cleanly as Code 128 on the shop's own printer.
    /// </summary>
    private async Task<string> ResolveBarcodeAsync(
        long productVariantId,
        string sku,
        CancellationToken cancellationToken)
    {
        var barcodes = await _barcodes
            .ListForVariantAsync(productVariantId, cancellationToken)
            .ConfigureAwait(false);

        BarcodeRecord? primary = null;

        foreach (var candidate in barcodes)
        {
            if (candidate.IsPrimary)
            {
                primary = candidate;
                break;
            }
        }

        primary ??= barcodes.Count > 0 ? barcodes[0] : null;

        return primary?.Value ?? sku;
    }
}

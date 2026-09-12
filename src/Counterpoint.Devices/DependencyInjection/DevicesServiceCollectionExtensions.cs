using System;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Devices.Labels;
using Counterpoint.Devices.Printing;
using Counterpoint.Devices.Printing.A4;
using Counterpoint.Devices.Printing.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Counterpoint.Devices.DependencyInjection;

/// <summary>
/// Wires the peripherals into the composition root. Only the composition root calls this -
/// Counterpoint.Ui never references this assembly (CLAUDE.md "Project boundaries").
/// </summary>
public static class DevicesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the receipt printer, the receipt renderer and the print outbox worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="printerOptions">
    /// Where the development printer writes, and whether it should pretend to be broken.
    /// Null uses the defaults.
    /// </param>
    /// <param name="workerOptions">Poll interval and retry budget. Null uses the defaults.</param>
    /// <param name="capabilities">
    /// What the shop's printer can be trusted to do. Null uses the standard 80 mm profile;
    /// <c>HW-T01</c> replaces it with the real unit's quirks.
    /// </param>
    /// <param name="labelPrinterOptions">
    /// Where the development label printer writes, and whether it should pretend to be broken.
    /// Null uses the defaults.
    /// </param>
    public static IServiceCollection AddCounterpointDevices(
        this IServiceCollection services,
        FileReceiptPrinterOptions? printerOptions = null,
        PrintWorkerOptions? workerOptions = null,
        PrinterCapabilities? capabilities = null,
        FileLabelPrinterOptions? labelPrinterOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(printerOptions ?? new FileReceiptPrinterOptions());
        services.AddSingleton(workerOptions ?? new PrintWorkerOptions());
        services.AddSingleton(capabilities ?? PrinterCapabilities.Default);

        // The file printer is the only implementation the software track has, on purpose. The
        // Windows raw spooler adapter is HW-T01's, and it swaps in here and nowhere else.
        services.AddSingleton<IReceiptPrinter, FileReceiptPrinter>();

        // P1-T11: the ZXing raster fallback for a printer whose native GS k / GS ( k cannot be
        // trusted (SRS FR-7.4). Registered unconditionally - EscPosRenderer only reaches it when
        // PrinterCapabilities.BarcodeMode is Raster, so this changes nothing for the default
        // Native profile above.
        services.AddSingleton<IBarcodeRasteriser, ZXingBarcodeRasteriser>();

        services.AddSingleton(provider => new EscPosRenderer(
            provider.GetRequiredService<PrinterCapabilities>(),
            provider.GetRequiredService<IBarcodeRasteriser>()));
        services.AddSingleton<ISaleReceiptRenderer, EscPosSaleReceiptRenderer>();

        // P1-T11: the A4/A5 invoice - a separate renderer over the same SaleReceipt data, never
        // sharing a layout engine with the 80 mm thermal path above (SRS FR-7.2, FR-7.9).
        services.AddSingleton<ISaleInvoiceRenderer, QuestPdfSaleInvoiceRenderer>();

        // P2-T06: the purchase order document (SRS FR-4.5) - the same QuestPDF/A4 approach as the
        // sale invoice above, over its own data model.
        services.AddSingleton<IPurchaseOrderDocumentRenderer, QuestPdfPurchaseOrderRenderer>();

        // P2-T07: the goods receipt note document (SRS FR-4.7, FR-7.10) - the same QuestPDF/A4
        // approach as the purchase order above, over its own data model.
        services.AddSingleton<IGoodsReceiptDocumentRenderer, QuestPdfGoodsReceiptRenderer>();

        // P1-T11: the settings screen's template preview - renders to text, never to the
        // printer or the outbox.
        services.AddSingleton<IReceiptTemplatePreviewService, ReceiptTemplatePreviewService>();

        // P1-T10: the cancellation slip (SRS FR-3.34) - the same renderer/capabilities pair as
        // the sale receipt above, over its own fixed layout.
        services.AddSingleton<ISaleCancellationReceiptRenderer, EscPosSaleCancellationReceiptRenderer>();

        // P2-T02: the return receipt (SRS FR-5, FR-7.1) - the same renderer/capabilities pair,
        // over its own fixed layout, the same shape as the cancellation slip above.
        services.AddSingleton<IReturnReceiptRenderer, EscPosReturnReceiptRenderer>();

        // P1-T12: the shelf-label printer - a separate device abstraction from the receipt
        // printer above, because most shelf-label printers speak TSPL rather than ESC/POS. The
        // Windows raw spooler adapter is HW-T03's, and it swaps in here and nowhere else.
        services.AddSingleton(labelPrinterOptions ?? new FileLabelPrinterOptions());
        services.AddSingleton<ILabelPrinter, FileLabelPrinter>();
        services.AddSingleton<ILabelRenderer, TsplLabelRenderer>();

        services.TryAddSingleton(TimeProvider.System);

        // Registered as a singleton and then handed to the host, rather than AddHostedService,
        // so a test can build the same container and drive one pass by hand without a host
        // starting a polling loop underneath it.
        services.AddSingleton<PrintWorker>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PrintWorker>());

        return services;
    }
}

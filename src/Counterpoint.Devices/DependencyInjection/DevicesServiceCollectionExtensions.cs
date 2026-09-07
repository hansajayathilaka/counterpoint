using System;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Devices.Printing;
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
    public static IServiceCollection AddCounterpointDevices(
        this IServiceCollection services,
        FileReceiptPrinterOptions? printerOptions = null,
        PrintWorkerOptions? workerOptions = null,
        PrinterCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(printerOptions ?? new FileReceiptPrinterOptions());
        services.AddSingleton(workerOptions ?? new PrintWorkerOptions());
        services.AddSingleton(capabilities ?? PrinterCapabilities.Default);

        // The file printer is the only implementation the software track has, on purpose. The
        // Windows raw spooler adapter is HW-T01's, and it swaps in here and nowhere else.
        services.AddSingleton<IReceiptPrinter, FileReceiptPrinter>();

        services.AddSingleton(provider => new EscPosRenderer(provider.GetRequiredService<PrinterCapabilities>()));
        services.AddSingleton<ISaleReceiptRenderer, EscPosSaleReceiptRenderer>();

        services.TryAddSingleton(TimeProvider.System);

        // Registered as a singleton and then handed to the host, rather than AddHostedService,
        // so a test can build the same container and drive one pass by hand without a host
        // starting a polling loop underneath it.
        services.AddSingleton<PrintWorker>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PrintWorker>());

        return services;
    }
}

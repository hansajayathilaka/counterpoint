using System;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Domain.Services;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.Ui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Counterpoint.App.DependencyInjection;

/// <summary>
/// The whole of the wiring: the adapters, the use cases and the screen.
/// </summary>
/// <remarks>
/// This is the only place in the solution that can see both <c>Counterpoint.Ui</c> and
/// <c>Counterpoint.Infrastructure</c>. Everything above meets everything below exactly here,
/// through interfaces, which is what makes "authorisation is checked in the Application layer,
/// not the UI" a structural fact rather than a promise (SRS NFR-S2, AC-17).
/// </remarks>
internal static class CounterpointHostBuilderExtensions
{
    /// <summary>Registers every service the application needs.</summary>
    internal static HostApplicationBuilder ConfigureCounterpoint(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Adapters. Resolving the data directory happens here, at start-up, so an unusable
        // folder is refused before the sales screen ever opens (engineering guide §4.9).
        builder.Services.AddCounterpointInfrastructure();
        builder.Services.AddCounterpointDevices();

        // The shop's rounding rule. Two decimal places for LKR (Q-01); it becomes a setting in
        // P1-T03, read from app_setting rather than fixed here.
        builder.Services.AddSingleton<IRoundingPolicy>(new HalfAwayFromZeroRounding(decimalPlaces: 2));

        // Use cases. Kept in step with the same three lines in
        // tests/Counterpoint.Integration.Tests/Sales/SaleFixture.cs, which composes the same
        // container without Avalonia in it.
        builder.Services.AddSingleton<IScanItem, ScanItemHandler>();
        builder.Services.AddSingleton<CompleteSaleHandler>();
        builder.Services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());
        builder.Services.AddSingleton<IQuoteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        // The screen.
        builder.Services.AddSingleton<SalesViewModel>();

        return builder;
    }
}

using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Reporting.Inventory;
using Counterpoint.Reporting.Shifts;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.Reporting.DependencyInjection;

/// <summary>
/// Wires the report queries into the composition root. Only the composition root calls this -
/// Counterpoint.Ui never references this assembly (CLAUDE.md "Project boundaries").
/// </summary>
/// <remarks>
/// <b>The owner-only interface is decorated here, not in the composition root</b>, the same
/// reasoning <c>Counterpoint.Backup</c>'s own <c>AddCounterpointBackup</c> already documents:
/// <see cref="IStockValuationQuery"/> is implemented by <see cref="StockValuationQuery"/>, a class
/// internal to this assembly, so only this extension - not <c>Counterpoint.App</c> - can name it
/// to build and wrap one with <see cref="RoleAuthorisation"/> (SRS NFR-S2, AC-17, CLAUDE.md
/// invariant 8).
/// </remarks>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reorder alert list, the stock valuation report and the slow-moving/
    /// non-moving stock report (task P2-T11).
    /// </summary>
    public static IServiceCollection AddCounterpointReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not owner-only: neither carries a cost or margin figure (CLAUDE.md invariant 8), the
        // same reasoning IStockEnquiry and IDashboardQueries already draw.
        services.AddSingleton<IReorderListQuery, ReorderListQuery>();
        services.AddSingleton<ISlowMovingStockQuery, SlowMovingStockQuery>();

        // P3-T02: the X report's own sales/returns/tax/tender figures. Not owner-only either -
        // none of the five figures is cost or margin, and the X report itself is a cashier
        // capability for their own shift (SRS FR-8.3, RPT-04); the per-shift "own shift only"
        // restriction is enforced in Counterpoint.Application.Shifts.XReportService, not here,
        // because it depends on which shift is asked for, not on the caller's role alone - the
        // same reasoning that keeps IShiftLookup and ICashMovementReader undecorated too.
        services.AddSingleton<IXReportFiguresReader, XReportFiguresReader>();

        // Owner-only: every figure is cost-derived. The concrete service is built inside the
        // factory and decorated; nothing can resolve an undecorated StockValuationQuery from the
        // container because there is no such registration, the same discipline
        // AddCounterpointSecurity draws for IUserAdministration.
        services.AddSingleton<IStockValuationQuery>(provider => RoleAuthorisation.Decorate<IStockValuationQuery>(
            ActivatorUtilities.CreateInstance<StockValuationQuery>(provider),
            provider.GetRequiredService<ISession>()));

        return services;
    }
}

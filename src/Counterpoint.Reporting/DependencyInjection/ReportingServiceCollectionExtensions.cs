using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Reporting.Inventory;
using Counterpoint.Reporting.Queries;
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
    /// Registers every report query this assembly implements: the reorder alert list, the stock
    /// valuation report and the slow-moving/non-moving stock report (task P2-T11); the X report's
    /// figures (P3-T02); and the canonical period query layer - the cashier-safe sales summary and
    /// the owner-only profit summary (P3-T04).
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

        // P3-T04: the canonical report query layer - one implementation of gross sales, net sales,
        // COGS, gross profit and the tender total, shared by every report (SRS FR-9.1-FR-9.6,
        // AC-12). PeriodFiguresReader is the shared engine both queries project from, and it is
        // deliberately *not* registered: ReadWithCogsAsync returns a cost figure with no role check
        // of its own, so a bare registration would let any future P3-T05/P3-T06 query inject it and
        // bypass [RequiresRole(Role.Owner)]. Each query builds its own instead, the same
        // decorated-only discipline IStockValuationQuery below and AddCounterpointSecurity's
        // IUserAdministration keep (CLAUDE.md invariant 8).

        // Not owner-only: SalesPeriodSummary carries no cost or margin field at all, so a cashier
        // session can run the figures RPT-01/RPT-02 need without ever being handed a cost figure
        // (CLAUDE.md invariant 8, SRS AC-17). Built through ActivatorUtilities rather than a bare
        // registration for the same reason: its constructor takes the connection factory, not a
        // registered engine.
        services.AddSingleton<ISalesPeriodSummaryQuery>(provider =>
            ActivatorUtilities.CreateInstance<SalesPeriodSummaryQuery>(provider));

        // Owner-only: every extra figure is cost-derived (COGS, gross profit, margin). Wrapped here,
        // not in the composition root, for the same reason IStockValuationQuery below is: the
        // concrete query is internal to this assembly, so only this extension can name it to build
        // and decorate one (SRS FR-9.4, CLAUDE.md invariant 8, AC-17).
        services.AddSingleton<IProfitPeriodSummaryQuery>(provider =>
            RoleAuthorisation.Decorate<IProfitPeriodSummaryQuery>(
                ActivatorUtilities.CreateInstance<ProfitPeriodSummaryQuery>(provider),
                provider.GetRequiredService<ISession>()));

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

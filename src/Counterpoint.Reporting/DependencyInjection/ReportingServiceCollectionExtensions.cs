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
    /// the owner-only profit summary (P3-T04); the sales, returns and profit reports (P3-T05); the tax,
    /// tender reconciliation, shift variance, stock card, damage, supplier purchase and fast-moving
    /// reports and the stock-on-hand list (P3-T06).
    /// </summary>
    public static IServiceCollection AddCounterpointReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not owner-only: neither carries a cost or margin figure (CLAUDE.md invariant 8), the
        // same reasoning IStockEnquiry and IDashboardQueries already draw.
        services.AddSingleton<IReorderListQuery, ReorderListQuery>();

        // P3-T06: the stock-on-hand list (SRS RPT-08, both roles). Cost-free by construction - no cost
        // column in its SQL or DTO - so it is registered plain.
        services.AddSingleton<IStockOnHandQuery, StockOnHandQuery>();

        // P3-T06: id and name for the category / brand / supplier filters of the two both-role stock
        // reports. Names only, so plain.
        services.AddSingleton<IReportFilterLookup, ReportFilterLookup>();

        // Owner-only since P3-T06: the slow-moving report now carries the value tied up (qty x cost_avg),
        // a cost figure, and SRS RPT-12 lists it for the owner role. Decorated here for the same reason
        // IStockValuationQuery is - the concrete query is internal, so only this extension can wrap it.
        services.AddSingleton<ISlowMovingStockQuery>(provider =>
            RoleAuthorisation.Decorate<ISlowMovingStockQuery>(
                ActivatorUtilities.CreateInstance<SlowMovingStockQuery>(provider),
                provider.GetRequiredService<ISession>()));

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

        // P3-T05: the sales, returns and profit reports. The three cashier-safe queries carry no
        // cost or margin field at all and never select a cost column, so they are registered plain
        // (CLAUDE.md invariant 8, SRS AC-17); each builds its own readers through
        // ActivatorUtilities, the same discipline as the period queries above.
        services.AddSingleton<ISalesSummaryReportQuery>(provider =>
            ActivatorUtilities.CreateInstance<SalesSummaryReportQuery>(provider));
        services.AddSingleton<ISalesBillQuery>(provider =>
            ActivatorUtilities.CreateInstance<SalesBillQuery>(provider));
        services.AddSingleton<ISalesBreakdownQuery>(provider =>
            ActivatorUtilities.CreateInstance<SalesBreakdownQuery>(provider));

        // Owner-only: RPT-03 reads cost (snapshot COGS) and returns margin (SRS FR-9.4). Decorated
        // here for the same reason IProfitPeriodSummaryQuery is - the concrete query is internal, so
        // only this extension can build and wrap one; there is no bare registration.
        services.AddSingleton<IProfitReportQuery>(provider =>
            RoleAuthorisation.Decorate<IProfitReportQuery>(
                ActivatorUtilities.CreateInstance<ProfitReportQuery>(provider),
                provider.GetRequiredService<ISession>()));

        // Owner-only: SRS section 9 lists RPT-14 (returns analysis) for the owner role.
        services.AddSingleton<IReturnsReportQuery>(provider =>
            RoleAuthorisation.Decorate<IReturnsReportQuery>(
                ActivatorUtilities.CreateInstance<ReturnsReportQuery>(provider),
                provider.GetRequiredService<ISession>()));

        // P3-T06: the remaining SRS section 9 reports. All owner-only, and all registered ONLY through
        // RoleAuthorisation.Decorate - each concrete query is internal to this assembly and there is no bare
        // registration of it. Each takes the connection factory (and, where it reads settings or composes
        // another query, that service) through ActivatorUtilities.
        services.AddSingleton<ITaxReportQuery>(provider =>
            RoleAuthorisation.Decorate<ITaxReportQuery>(
                ActivatorUtilities.CreateInstance<TaxReportQuery>(provider),
                provider.GetRequiredService<ISession>()));

        services.AddSingleton<ITenderReconciliationQuery>(provider =>
            RoleAuthorisation.Decorate<ITenderReconciliationQuery>(
                ActivatorUtilities.CreateInstance<TenderReconciliationQuery>(provider),
                provider.GetRequiredService<ISession>()));

        services.AddSingleton<IShiftVarianceHistoryQuery>(provider =>
            RoleAuthorisation.Decorate<IShiftVarianceHistoryQuery>(
                ActivatorUtilities.CreateInstance<ShiftVarianceHistoryQuery>(provider),
                provider.GetRequiredService<ISession>()));

        services.AddSingleton<IStockCardQuery>(provider =>
            RoleAuthorisation.Decorate<IStockCardQuery>(
                ActivatorUtilities.CreateInstance<StockCardQuery>(provider),
                provider.GetRequiredService<ISession>()));

        services.AddSingleton<IDamageAdjustmentReportQuery>(provider =>
            RoleAuthorisation.Decorate<IDamageAdjustmentReportQuery>(
                ActivatorUtilities.CreateInstance<DamageAdjustmentReportQuery>(provider),
                provider.GetRequiredService<ISession>()));

        services.AddSingleton<ISupplierPurchaseReportQuery>(provider =>
            RoleAuthorisation.Decorate<ISupplierPurchaseReportQuery>(
                ActivatorUtilities.CreateInstance<SupplierPurchaseReportQuery>(provider),
                provider.GetRequiredService<ISession>()));

        // Owner-only (SRS RPT-13) though cost-free: a thin projection over the plain ISalesBreakdownQuery.
        services.AddSingleton<IFastMovingReportQuery>(provider =>
            RoleAuthorisation.Decorate<IFastMovingReportQuery>(
                ActivatorUtilities.CreateInstance<FastMovingReportQuery>(provider),
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

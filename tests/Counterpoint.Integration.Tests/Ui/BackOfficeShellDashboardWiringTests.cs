using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Import;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Catalogue;
using Counterpoint.Ui.ViewModels.Dashboard;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T20's own quick-action and reload wiring on <see cref="BackOfficeShellViewModel"/>,
/// over a real SQLite database (<see cref="SaleFixture"/>) - never the in-memory EF Core provider.
/// The KPI/reorder/recent-sales figures themselves are <see cref="DashboardViewModelTests"/>'s job;
/// this file is about the shell-level wiring around <see cref="DashboardViewModel"/>: when it
/// reloads, and the two quick-action bindings (<see cref="BackOfficeShellViewModel.CanOpenShift"/>,
/// <see cref="BackOfficeShellViewModel.ReturnToSalesCommand"/>) this task adds.
/// </summary>
public sealed class BackOfficeShellDashboardWiringTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task UI_16_CanOpenShiftReflectsWhetherTheSessionHasAnOpenShift()
    {
        // Mirrors SalesViewModel.CanOpenShift's own proof (ShiftOpenAndDashboardScreenTests):
        // close the seeded shift before signing in, so the session starts with none open.
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.SignInAsSeededOwnerAsync();

        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());

        shell.CanOpenShift.Should().BeTrue("no shift is open yet");

        var user = fixture.Resolve<ISession>().CurrentUser!;
        await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(5000m), SoldAt));

        shell.CanOpenShift.Should().BeFalse("a shift is now open - the Overview's \"Open shift\" button has nothing left to do");
    }

    [Fact]
    public async Task UI_16_ReturnToSalesCommandRaisesReturnToSalesRequestedExactlyOnce()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());

        var raisedCount = 0;
        shell.ReturnToSalesRequested += (_, _) => raisedCount++;

        shell.ReturnToSalesCommand.Execute(null);

        raisedCount.Should().Be(1, "both the Overview's \"New sale\" and \"Open shift\" buttons bind to this one command, "
            + "and App.axaml.cs's ShowBackOffice closes the back-office window on exactly one event per press");
    }

    [Fact]
    public async Task UI_16_LeavingCatalogueBackToOverviewReloadsTheDashboardButSelectingACatalogueSectionDoesNot()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var dashboard = new DashboardViewModel(
            fixture.Resolve<IDashboardQueries>(),
            fixture.Resolve<IReorderListQuery>(),
            fixture.Resolve<IRecentSalesQuery>());

        var dialogs = new FakeDialogService();
        var catalogue = new CatalogueViewModel(
            new CategoryTabViewModel(fixture.Resolve<ICategoryMaintenance>(), dialogs),
            new BrandTabViewModel(fixture.Resolve<IBrandMaintenance>(), dialogs),
            new UomTabViewModel(fixture.Resolve<IUomMaintenance>(), dialogs),
            new TaxClassTabViewModel(fixture.Resolve<ITaxClassMaintenance>(), dialogs),
            new SupplierTabViewModel(fixture.Resolve<ISupplierMaintenance>(), dialogs),
            new CustomerTabViewModel(fixture.Resolve<ICustomerMaintenance>(), dialogs),
            new ProductTabViewModel(
                fixture.Resolve<IProductMaintenance>(),
                fixture.Resolve<ICategoryMaintenance>(),
                fixture.Resolve<IBrandMaintenance>(),
                fixture.Resolve<IUomMaintenance>(),
                fixture.Resolve<ITaxClassMaintenance>(),
                dialogs),
            new ImportTabViewModel(fixture.Resolve<ICatalogueImportService>(), fixture.Resolve<ISpreadsheetReader>()));

        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());
        shell.AttachDashboard(dashboard);
        shell.AttachCatalogue(catalogue);

        // The unconditional load App.axaml.cs's ShowBackOffice performs every time the shell
        // opens on Overview.
        await dashboard.LoadCommand.ExecuteAsync(null);
        dashboard.BillCountText.Should().Be("0");

        // A sale completes while the owner is looking at something else - the stale-figure
        // scenario this reload rule exists to close.
        await CompleteOneAsync(fixture);

        // Selecting a Catalogue section for the first time must not touch the dashboard at all.
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        dashboard.BillCountText.Should().Be(
            "0", "selecting a Catalogue section must not reload Overview's dashboard");

        // Moving between Catalogue sections within the same visit must not reload it either.
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[1];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        dashboard.BillCountText.Should().Be("0");

        // Leaving Catalogue back to Overview must reload it.
        shell.SelectedCatalogueSection = null;
        await (dashboard.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        dashboard.BillCountText.Should().Be(
            "1", "leaving Catalogue back to Overview must reload the dashboard");
    }

    private static async Task CompleteOneAsync(SaleFixture fixture)
    {
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
        var lines = new List<SaleLineRequest> { new(variantId, 1m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            SoldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }
}

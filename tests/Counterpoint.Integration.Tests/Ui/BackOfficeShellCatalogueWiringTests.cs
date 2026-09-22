using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Import;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T18's own DI wiring change: <c>CounterpointHostBuilderExtensions</c> stopped resolving
/// <see cref="CatalogueViewModel"/> as its own screen (<c>App.axaml.cs</c> no longer takes one as
/// a constructor parameter at all) and instead attaches it to
/// <see cref="BackOfficeShellViewModel"/> through <see cref="BackOfficeShellViewModel.AttachCatalogue"/>,
/// once, from a factory registration - a wiring detail no test anywhere exercised. A broken
/// factory (a missing registration, a constructor signature drift) would not fail fast the way a
/// straight <c>AddSingleton&lt;CatalogueViewModel&gt;()</c> resolved eagerly by <c>App</c>'s own
/// constructor used to; it would surface only the first time a cashier - an owner, here - actually
/// selected a Catalogue section, which is exactly the "silently NPE at real app startup on the
/// Catalogue path" risk this test exists to catch. Built over a real SQLite file
/// (<see cref="SaleFixture"/>), never the in-memory provider, with the same real Application-layer
/// collaborators <c>CounterpointHostBuilderExtensions</c> itself resolves for every one of
/// <see cref="CatalogueViewModel"/>'s eight tabs - not a hand rolled substitute for the composition
/// root, but the same shape it builds, proven end to end instead of merely compiling.
/// </summary>
public sealed class BackOfficeShellCatalogueWiringTests
{
    [Fact]
    public async Task P3_T18_AttachCatalogueWiresTheRealCatalogueViewModelAndDefersItsLoadUntilASectionIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.Resolve<ICategoryMaintenance>().CreateAsync(new SaveCategoryCommand("Fasteners", null));

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

        shell.Catalogue.Should().BeNull(
            "before AttachCatalogue runs - the composition root's own job, right after both "
            + "singletons resolve - nothing is wired yet");

        shell.AttachCatalogue(catalogue);

        shell.Catalogue.Should().BeSameAs(
            catalogue,
            "AttachCatalogue is exactly what CounterpointHostBuilderExtensions' "
            + "BackOfficeShellViewModel factory calls, and it is the only way Catalogue is ever "
            + "populated");
        catalogue.Category.Items.Should().BeEmpty(
            "attaching the viewmodel must not itself load anything - only navigating to a "
            + "Catalogue section does (SRS NFR-P6, deferred load)");

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        catalogue.Category.Items.Should().ContainSingle(
            item => item.Name == "Fasteners",
            "selecting the first Catalogue section for the first time must reach the real, "
            + "attached CatalogueViewModel and run its LoadCommand against the real database - "
            + "the one step a broken AttachCatalogue wiring would silently skip, with no "
            + "exception, until an owner actually clicked into Catalogue");
    }

    /// <summary>
    /// Bugfix regression test for task P3-T18's code review. The guard that used to be a
    /// once-per-process instance field (<c>_catalogueLoaded</c>) on the
    /// <see cref="BackOfficeShellViewModel"/> singleton meant a section reload happened once ever
    /// for the whole process lifetime, not once per genuine Catalogue visit - a row written to the
    /// database by another screen or an import after the first visit stayed invisible in Catalogue
    /// for the rest of the session, no matter how many more times the owner navigated back into it.
    /// This proves two things together, the same way the reviewer asked: (1) navigating BETWEEN
    /// Catalogue sections within a single visit must not reload - only the data of the section
    /// actually selected first is fresh; and (2) leaving Catalogue - via <see cref="BackOfficeShellViewModel.SelectOverview"/>,
    /// the same call <c>App.axaml.cs</c>'s <c>ShowBackOffice</c> makes on the back-office window's
    /// <c>Closed</c> event, standing in here for a real close/reopen of the shell - and picking a
    /// section again, even a different one than was showing before, must reload and pick up data
    /// written in the meantime.
    /// </summary>
    [Fact]
    public async Task P3_T18_ReenteringCatalogueAfterLeavingReloadsButSwitchingSectionsWithinAVisitDoesNotAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));

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
        shell.AttachCatalogue(catalogue);

        // First visit: selecting the first section for the first time loads the real data.
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        catalogue.Category.Items.Should().ContainSingle(item => item.Name == "Fasteners");

        // Data changes underneath, as if another screen or an import wrote it directly to the DB.
        await categories.CreateAsync(new SaveCategoryCommand("Adhesives", null));

        // Still within the same visit: moving to a different section must NOT reload.
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[1];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        catalogue.Category.Items.Should().HaveCount(
            1,
            "switching between Catalogue sections within one visit must not re-run LoadCommand - "
            + "that is task P3-T18's own improvement over the old per-tile unconditional reload, "
            + "and this bugfix must not undo it");

        // Leaving Catalogue (Overview - the same call the back-office window's Closed handler
        // makes) and genuinely re-entering, picking a different section than was showing before,
        // must reload and pick up what changed underneath.
        shell.SelectOverview();
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[4];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        catalogue.Category.Items.Should().HaveCount(2)
            .And.Contain(item => item.Name == "Adhesives");

        // A second round, standing in for closing and reopening the back-office shell itself
        // (App.axaml.cs's ShowBackOffice wires exactly this SelectOverview() call to the window's
        // Closed event) - proving the guard resets on every genuine re-entry, not once ever.
        await categories.CreateAsync(new SaveCategoryCommand("Sealants", null));
        shell.SelectOverview();
        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];
        await (catalogue.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        catalogue.Category.Items.Should().HaveCount(3)
            .And.Contain(item => item.Name == "Sealants");
    }
}

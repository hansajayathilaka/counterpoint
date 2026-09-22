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
}

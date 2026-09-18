using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// <b>AC-23</b> — "Every add/edit/delete action uses the shared dialog component, and each
/// dialog's heading states unambiguously whether it is creating a new record or editing an
/// identified existing one." Task P3-T16's closing gate for the whole UI redesign.
/// </summary>
/// <remarks>
/// <para>
/// Every screen this proves already has its own dedicated dialog test file
/// (<c>CategoryTabViewModelDialogTests</c>, <c>BrandTabViewModelDialogTests</c>,
/// <c>UomTabViewModelDialogTests</c>, <c>TaxClassTabViewModelDialogTests</c>,
/// <c>SupplierTabViewModelDialogTests</c>, <c>CustomerTabViewModelDialogTests</c>,
/// <c>ProductTabViewModelDialogTests</c>) and <see cref="EditDialogWindowViewModelTests"/> proves
/// the shell's header logic itself is a pure function of the explicit <c>DialogMode</c>. This
/// class does not repeat any of that field-by-field; it is the one place that asks the single
/// question AC-23 actually poses - does every screen in the repository that can create, edit or
/// delete a record route that action through <see cref="IDialogService"/> with the right mode and
/// the right subject - about every one of them at once, plus <see cref="UserAdminViewModel"/>'s
/// own New/Reset-password dialogs, task P3-T16's own retrofit.
/// </para>
/// <para>
/// Driven end to end over a real encrypted database through <see cref="FakeDialogService"/>,
/// exactly the way every dialog test file above already is (a real <c>EditDialogWindow</c> cannot
/// be opened in CI).
/// </para>
/// </remarks>
public sealed class AC23_EveryEditActionUsesTheSharedDialogWithAnUnambiguousHeader
{
    [Fact]
    public async Task AC_23_TheCategoryScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var createdId = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));

        var dialogs = new FakeDialogService();
        var screen = new CategoryTabViewModel(categories, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "category");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "category", "Fasteners");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "category", "Fasteners");
    }

    [Fact]
    public async Task AC_23_TheBrandScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var createdId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));

        var dialogs = new FakeDialogService();
        var screen = new BrandTabViewModel(brands, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "brand");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "brand", "Bosch");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "brand", "Bosch");
    }

    [Fact]
    public async Task AC_23_TheUnitScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var createdId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var dialogs = new FakeDialogService();
        var screen = new UomTabViewModel(uoms, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "unit");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "unit", "Box");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "unit", "Box");
    }

    [Fact]
    public async Task AC_23_TheTaxClassScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var createdId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Standard", TaxRate.FromPercent(15m)));

        var dialogs = new FakeDialogService();
        var screen = new TaxClassTabViewModel(taxClasses, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "tax class");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "tax class", "Standard");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "tax class", "Standard");
    }

    [Fact]
    public async Task AC_23_TheSupplierScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();
        var createdId = await suppliers.CreateAsync(
            new SaveSupplierCommand("Acme Hardware", null, null, null, null, null));

        var dialogs = new FakeDialogService();
        var screen = new SupplierTabViewModel(suppliers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "supplier");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "supplier", "Acme Hardware");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "supplier", "Acme Hardware");
    }

    [Fact]
    public async Task AC_23_TheCustomerScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();
        var createdId = await customers.CreateAsync(
            new SaveCustomerCommand("Walk-in", null, null, null, "RETAIL", Money.Zero));

        var dialogs = new FakeDialogService();
        var screen = new CustomerTabViewModel(customers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "customer");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "customer", "Walk-in");

        dialogs.ConfirmDeletes = false;
        await screen.DeleteCommand.ExecuteAsync(null);
        AssertDelete(dialogs, "customer", "Walk-in");
    }

    [Fact]
    public async Task AC_23_TheProductScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var pieceId = await uoms.CreateAsync(new SaveUomCommand("Test unit piece", "pc", 0));
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var taxClassId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Test exempt", TaxRate.Zero));

        var products = fixture.Resolve<IProductMaintenance>();
        var createdId = await products.CreateAsync(new SaveProductCommand(
            "HAMMER-1", "Hammer", null, null, null, pieceId, ProductType.Standard, taxClassId,
            null, false, null, null, null));

        var dialogs = new FakeDialogService();
        var screen = new ProductTabViewModel(
            products,
            fixture.Resolve<ICategoryMaintenance>(),
            fixture.Resolve<IBrandMaintenance>(),
            uoms,
            taxClasses,
            dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewProductDialogCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "product");

        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);
        await screen.EditProductDialogCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "product", "Hammer");

        // No Delete on this screen (FR-2.1's own pattern - deactivate, never delete, applied to
        // products since P1-T05); ToggleActiveCommand stays the inline action, exactly as
        // CategoryTabViewModel's own toggle-active button does. Nothing to assert here beyond
        // Create/Edit, which is the full AC-23 shape this screen actually has.
    }

    [Fact]
    public async Task AC_23_TheUserScreenAsksTheSharedDialogForTheRightModeAndSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var users = fixture.Resolve<IUserAdministration>();

        var dialogs = new FakeDialogService
        {
            // Stands in for what the operator would type into UserEditView before Save.
            ConfigureContent = content =>
            {
                var userContent = (UserEditViewModel)content;
                userContent.Username = "priya";
                userContent.DisplayName = "Priya";
                userContent.Password = "counter1";
            },
        };
        var screen = new UserAdminViewModel(users, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewUserDialogCommand.ExecuteAsync(null);
        AssertCreate(dialogs, "user");

        screen.SelectedUser = screen.Users.Single(user => user.Username == "priya");
        await screen.ResetPasswordDialogCommand.ExecuteAsync(null);
        AssertEdit(dialogs, "user", "priya");

        // No Delete on this screen (SRS FR-1.4: an account is deactivated, never deleted);
        // ToggleActiveCommand stays the inline action, the same choice every catalogue tab's own
        // toggle-active button already makes.
    }

    private static void AssertCreate(FakeDialogService dialogs, string entityName)
    {
        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[^1].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[^1].EntityName.Should().Be(entityName);
        dialogs.EditRequests[^1].SubjectDescription.Should().BeNull(
            "a create dialog has no existing record to name yet (SRS UI-15)");

        dialogs.EditRequests.RemoveAt(dialogs.EditRequests.Count - 1);
    }

    private static void AssertEdit(FakeDialogService dialogs, string entityName, string subject)
    {
        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[^1].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[^1].EntityName.Should().Be(entityName);
        dialogs.EditRequests[^1].SubjectDescription.Should().Be(
            subject, "an edit dialog's header must name the specific record it is acting on (SRS UI-15)");

        dialogs.EditRequests.RemoveAt(dialogs.EditRequests.Count - 1);
    }

    private static void AssertDelete(FakeDialogService dialogs, string entityName, string subject)
    {
        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[^1].EntityName.Should().Be(entityName);
        dialogs.DeleteRequests[^1].SubjectDescription.Should().Be(
            subject, "a delete confirmation must name the specific record before anything happens (SRS UI-05)");
    }
}

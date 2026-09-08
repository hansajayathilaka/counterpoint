using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels.FirstRun;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Settings;

/// <summary>
/// The first-run wizard, driven from its viewmodel over a real encrypted database
/// (P1-T03 "Do this" 5, SRS FR-10, FR-1.3).
/// </summary>
/// <remarks>
/// The point of these tests is that the wizard is a thin adapter: the pages collect a
/// <see cref="FirstRunSetupRequest"/> and hand it to <see cref="IFirstRunSetup"/>, which is
/// already tested in its own right. So the last test drives the screen on one database and calls
/// the service directly with the screen's own request on another, and expects the two shops to be
/// identical.
/// </remarks>
public sealed class FirstRunWizardScreenTests
{
    private const string OwnerPassword = "shopkeeper2026";
    private const string BackupPassphrase = "correct horse battery staple";

    [Fact]
    public async Task FR_10_TheWizardProducesAUsableSystemFromAnEmptyDatabase()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IFirstRunSetup>();

        (await setup.IsRequiredAsync()).Should().BeTrue("nothing has configured this database");

        var wizard = Open(fixture);
        Fill(wizard);

        var completed = 0;
        wizard.Completed += (_, _) => completed++;

        await wizard.FinishCommand.ExecuteAsync(null);

        wizard.Status.Should().Be("The till is set up. Sign in to start trading.");
        completed.Should().Be(1, "the composition root opens the sign-in screen next");

        // 1. The settings the pages collected are in force, without a reload.
        var settings = fixture.Resolve<ISettings>();
        settings.Shop.Name.Should().Be("Nimal Hardware");
        settings.Financial.DecimalPlaces.Should().Be(2);
        settings.Numbering.Bill.Prefix.Should().Be("NH-");
        settings.Peripherals.ReceiptPrinterName.Should().Be("Counter printer");
        settings.Backup.LocalPath.Should().Be("/srv/counterpoint/backups");
        settings.Backup.PassphraseIsSet.Should().BeTrue();

        // 2. And persisted, so a restart finds them.
        (await fixture.ScalarAsync("SELECT value FROM app_setting WHERE key = 'shop.name';"))
            .Should().Be("Nimal Hardware");

        // 3. The shop's tax classes exist.
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Exempt';"))
            .Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Standard';"))
            .Should().Be(1);

        // 4. The bill series is numbered the way the wizard asked.
        (await fixture.ScalarAsync("SELECT prefix FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("NH-");

        // 5. The passphrase is in the protected store, not in the database.
        fixture.Resolve<IBackupPassphraseStore>().HasPassphrase().Should().BeTrue();
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE value LIKE '%correct horse%';"))
            .Should().Be(0, "NFR-S6");

        // 6. And the owner can sign in - which is what "usable" finally means.
        var login = await fixture.Resolve<IAuthenticationService>()
            .LogInAsync("owner", OwnerPassword);

        login.Succeeded.Should().BeTrue(login.Message);

        (await setup.IsRequiredAsync()).Should().BeFalse("the wizard does not run twice");
        wizard.OwnerPassword.Should().BeEmpty("a password is not left sitting in a box");
    }

    [Fact]
    public async Task FR_10_TheWizardIsAThinAdapterOverTheHeadlessSetup()
    {
        await using var throughTheScreen = await SaleFixture.CreateAsync();
        await using var throughTheService = await SaleFixture.CreateAsync();

        var screenWizard = Open(throughTheScreen);
        Fill(screenWizard);
        await screenWizard.FinishCommand.ExecuteAsync(null);

        // The same pages, filled the same way, but handed to the Application service directly.
        var serviceWizard = Open(throughTheService);
        Fill(serviceWizard);

        var configured = await throughTheService.Resolve<IFirstRunSetup>()
            .CompleteAsync(serviceWizard.BuildRequest());

        configured.Should().BeTrue();

        // The two shops are the same shop. PassphraseIsSet is the one value that comes from the
        // machine rather than from the request, and both machines have one.
        throughTheScreen.Resolve<ISettings>().Current
            .Should().Be(
                throughTheService.Resolve<ISettings>().Current,
                "the screen adds nothing of its own - it fills in a request and hands it over");

        (await throughTheScreen.CountAsync("SELECT COUNT(*) FROM tax_class;"))
            .Should().Be(await throughTheService.CountAsync("SELECT COUNT(*) FROM tax_class;"));

        (await throughTheScreen.ScalarAsync(
            "SELECT prefix || '|' || pattern || '|' || next_val FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be(await throughTheService.ScalarAsync(
                "SELECT prefix || '|' || pattern || '|' || next_val FROM number_sequence WHERE doc_type = 'SALE';"));
    }

    [Fact]
    public async Task FR_1_3_TheWizardWillNotFinishWithoutAnOwnerPasswordTypedTwice()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IFirstRunSetup>();

        var wizard = Open(fixture);
        Fill(wizard);
        wizard.ConfirmOwnerPassword = "something else";

        await wizard.FinishCommand.ExecuteAsync(null);

        wizard.Status.Should().Be("The two passwords are not the same. Type them again.");
        wizard.StepIndex.Should().Be(5, "it takes the owner back to the page that needs them");

        (await setup.IsRequiredAsync()).Should().BeTrue("nothing was written");
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Standard';"))
            .Should().Be(
                0,
                "close the window on page six and the database is still an un-set-up database");
    }

    [Fact]
    public async Task UI_06_TheWizardWillNotLeaveAPageThatIsMissingSomething()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var wizard = Open(fixture);
        wizard.NextCommand.Execute(null);

        wizard.StepIndex.Should().Be(0);
        wizard.Status.Should().Be("The shop's name prints at the top of every bill. Please type it.");

        wizard.Shop.Name = "Nimal Hardware";
        wizard.NextCommand.Execute(null);

        wizard.StepIndex.Should().Be(1);
        wizard.CurrentStepHeading.Should().Be("Step 2 of 7 - Currency and rounding");

        wizard.BackCommand.Execute(null);
        wizard.StepIndex.Should().Be(0);
        wizard.Shop.Name.Should().Be("Nimal Hardware", "going back loses nothing");
    }

    [Fact]
    public async Task FR_10_3_TheWizardKeepsAtLeastOneTaxClass()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var wizard = Open(fixture);
        wizard.TaxClasses.Should().ContainSingle("the default class the shop starts with");

        wizard.RemoveTaxClassCommand.Execute(wizard.TaxClasses[0]);
        wizard.TaxClasses.Should().ContainSingle("every product needs a class, even a zero-rated one");

        wizard.AddTaxClassCommand.Execute(null);
        wizard.TaxClasses.Should().HaveCount(2);

        wizard.RemoveTaxClassCommand.Execute(wizard.TaxClasses[1]);
        wizard.TaxClasses.Should().ContainSingle();
    }

    [Fact]
    public async Task P1_T03_TheWizardsStartingNumberBoxIsLive()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var wizard = Open(fixture);

        wizard.BillNumbering.IsStartingNumberEditable.Should().BeTrue(
            "the wizard's starting number really does set the counter "
            + "(INumberSequenceConfiguration.InitialiseAsync), unlike the general settings screen");
    }

    /// <summary>Builds the wizard and opens it, which fills the pages with the shop's defaults.</summary>
    private static FirstRunWizardViewModel Open(SaleFixture fixture)
    {
        var wizard = new FirstRunWizardViewModel(
            fixture.Resolve<IFirstRunSetup>(),
            fixture.Resolve<ISettings>());

        wizard.LoadCommand.Execute(null);
        return wizard;
    }

    /// <summary>Everything a person would type, on all seven pages.</summary>
    private static void Fill(FirstRunWizardViewModel wizard)
    {
        wizard.Shop.Name = "Nimal Hardware";
        wizard.Shop.AddressLine1 = "142 Galle Road";
        wizard.Shop.Phone = "038-2233445";

        wizard.Financial.DecimalPlaces = "2";

        wizard.Tax.DefaultTaxClassName = "Exempt";
        wizard.TaxClasses[0].Name = "Exempt";
        wizard.TaxClasses[0].Rate = "0";
        wizard.AddTaxClassCommand.Execute(null);
        wizard.TaxClasses[1].Name = "Standard";
        wizard.TaxClasses[1].Rate = "15";

        wizard.BillNumbering.Prefix = "NH-";

        wizard.Peripherals.ReceiptPrinterName = "Counter printer";

        wizard.OwnerUsername = "owner";
        wizard.OwnerPassword = OwnerPassword;
        wizard.ConfirmOwnerPassword = OwnerPassword;

        wizard.Backup.LocalPath = "/srv/counterpoint/backups";
        wizard.Backup.NewPassphrase = BackupPassphrase;
        wizard.Backup.ConfirmPassphrase = BackupPassphrase;
    }
}

using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.Services;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels.Settings;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Settings;

/// <summary>
/// The settings screen, driven from its viewmodel over a real encrypted database
/// (SRS FR-10.1-10.9, NFR-M1, UI-06, UI-10).
/// </summary>
/// <remarks>
/// The viewmodel is exercised directly rather than through a window, because a window cannot be
/// opened in CI. Everything below it - <see cref="SettingsService"/>, the audit trail, the
/// SQLite adapters and the real encrypted file - is the production wiring, composed by
/// <see cref="SaleFixture"/> exactly as <c>Counterpoint.App</c> composes it. Nothing here is
/// mocked, so a green test means the shop's settings really did move.
/// </remarks>
public sealed class SettingsScreenTests
{
    [Fact]
    public async Task FR_10_1_to_10_8_EveryGroupRoundTripsThroughTheRealSaveAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        using var screen = Open(fixture);

        // FR-10.1 shop profile.
        screen.Shop.Name = "Nimal Hardware";
        screen.Shop.Phone = "038-2233445";
        screen.Shop.TaxRegistrationNumber = "TAX-99887766";

        // FR-10.2 financial.
        screen.Financial.DecimalPlaces = "3";
        screen.Financial.SymbolPositionChoice = screen.Financial.SymbolPositionChoices[1];
        screen.Financial.RoundingRuleChoice = screen.Financial.RoundingRuleChoices[1];

        // FR-10.3 tax.
        screen.Tax.DefaultTaxClassName = "Standard";
        screen.Tax.DefaultTaxRate = "15";
        screen.Tax.TaxLabel = "Tax on goods";
        screen.Tax.PricesIncludeTax = false;

        // FR-10.4 numbering.
        screen.Numbering.Bill.Prefix = "NH-";
        screen.Numbering.Bill.StartingNumber = "500";

        // FR-10.5 policy.
        screen.Policy.ReturnWindowDays = "30";
        screen.Policy.CashRefundLimit = "2500.50";
        screen.Policy.MaxLineDiscountRate = "12.5";
        screen.Policy.NegativeStockChoice = screen.Policy.NegativeStockChoices[2];

        // FR-10.6 peripherals.
        screen.Peripherals.ReceiptPrinterName = "Counter printer";
        screen.Peripherals.PaperWidthMm = "58";

        // FR-10.7 backup.
        screen.Backup.DailyBackupTime = "21:30";
        screen.Backup.LocalPath = "/srv/counterpoint/backups";
        screen.Backup.RetentionDays = "45";

        // FR-10.8 receipt.
        screen.Receipt.FooterText = "Bohoma sthuthi";
        screen.Receipt.ShowCustomerName = false;

        screen.HasUnsavedChanges.Should().BeTrue("the screen knows it is holding edits");

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be("Saved.");
        screen.HasUnsavedChanges.Should().BeFalse("everything the screen was holding is now saved");

        // The settings in force - not the object the screen handed over.
        var current = settings.Current;
        current.Shop.Name.Should().Be("Nimal Hardware");
        current.Shop.TaxRegistrationNumber.Should().Be("TAX-99887766");
        current.Financial.DecimalPlaces.Should().Be(3);
        current.Financial.SymbolPosition.Should().Be(CurrencySymbolPosition.After);
        current.Financial.RoundingRule.Should().Be(RoundingRule.HalfToEven);
        current.Tax.DefaultTaxClassName.Should().Be("Standard");
        current.Tax.DefaultTaxRate.AsPercent.Should().Be(15m);
        current.Tax.PricesIncludeTax.Should().BeFalse();
        current.Numbering.Bill.Prefix.Should().Be("NH-");
        current.Numbering.Bill.StartingNumber.Should().Be(500);
        current.Policy.ReturnWindowDays.Should().Be(30);
        current.Policy.CashRefundLimit.Amount.Should().Be(2500.50m);
        current.Policy.MaxLineDiscountRate.AsPercent.Should().Be(12.5m);
        current.Policy.NegativeStock.Should().Be(NegativeStockPolicy.Block);
        current.Peripherals.ReceiptPrinterName.Should().Be("Counter printer");
        current.Peripherals.PaperWidthMm.Should().Be(58);
        current.Backup.DailyBackupTime.Hour.Should().Be(21);
        current.Backup.DailyBackupTime.Minute.Should().Be(30);
        current.Backup.RetentionDays.Should().Be(45);
        current.Receipt.FooterText.Should().Be("Bohoma sthuthi");
        current.Receipt.ShowCustomerName.Should().BeFalse();

        // And on disk, so a restart finds them.
        (await fixture.ScalarAsync("SELECT value FROM app_setting WHERE key = 'shop.name';"))
            .Should().Be("Nimal Hardware");
        (await fixture.ScalarAsync(
            "SELECT value FROM app_setting WHERE key = 'policy.cash_refund_limit';"))
            .Should().Be("25005000", "money is a scaled integer, never a floating point number");

        // The bill series the screen renamed reaches number_sequence, not just app_setting.
        (await fixture.ScalarAsync("SELECT prefix FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("NH-");
    }

    [Fact]
    public async Task FR_10_2_ChangingTheDecimalPlacesOnTheScreenChangesRoundingWithoutARestart()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        // The very same IRoundingPolicy instance the sale path was handed at start-up.
        var rounding = fixture.Resolve<IRoundingPolicy>();
        rounding.DecimalPlaces.Should().Be(2);

        using var screen = Open(fixture);
        screen.Financial.DecimalPlaces = "3";
        await screen.SaveCommand.ExecuteAsync(null);

        rounding.DecimalPlaces.Should().Be(
            3,
            "nothing was rebuilt and nothing was restarted; the policy reads the setting (FR-10.2)");
    }

    [Fact]
    public async Task FR_10_9_SavingFromTheScreenAuditsEveryChangedKey()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        using var screen = Open(fixture);

        // The first save on a fresh database materialises every key it owns, which is one audit
        // row each. The baseline is taken after that, so what is counted is the owner's edit.
        await screen.SaveCommand.ExecuteAsync(null);
        var baseline = await AuditCountAsync(fixture);

        screen.Shop.Name = "Nimal Hardware";
        screen.Policy.ReturnWindowDays = "30";
        await screen.SaveCommand.ExecuteAsync(null);

        (await AuditCountAsync(fixture)).Should().Be(
            baseline + 2,
            "two changed settings are two audit rows, and nothing else moved (FR-10.9)");
    }

    [Fact]
    public async Task UI_10_ANumericBoxDropsWhatCannotBePartOfANumberWithoutAWord()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        using var screen = Open(fixture);

        screen.Policy.MaxLineDiscountRate = "abc";
        screen.Policy.MaxLineDiscountRate.Should().BeEmpty("letters are not part of a rate");

        screen.Policy.MaxLineDiscountRate = "12.5%";
        screen.Policy.MaxLineDiscountRate.Should().Be("12.5", "the stray keystroke is dropped");

        screen.Policy.CashRefundLimit = "-40";
        screen.Policy.CashRefundLimit.Should().Be(
            "40",
            "a cash refund limit cannot be negative, so the minus never reaches the box");

        screen.Policy.ReturnWindowDays = "1.5";
        screen.Policy.ReturnWindowDays.Should().Be("15", "a day count has no decimal point");

        screen.Status.Should().Be(
            "Showing the settings the shop is trading on.",
            "a stray keystroke does not raise a dialog while a customer is waiting");
    }

    [Fact]
    public async Task UI_06_AValueTheShopCannotTradeOnComesBackAsASentenceAndNothingIsSaved()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        using var screen = Open(fixture);
        screen.Policy.MaxLineDiscountRate = "150";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be(
            "The line discount limit must be between 0% and 100%; 150% was given.",
            "the Application layer refused it, in plain language, and the screen shows what it "
            + "was told without an exception name or a parameter in it");

        settings.Current.Policy.MaxLineDiscountRate.AsPercent.Should().Be(
            100m,
            "a refused save changes nothing");
    }

    [Fact]
    public async Task UI_06_AnUnreadableBackupTimeIsRefusedBeforeAnythingIsWritten()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        using var screen = Open(fixture);
        screen.Backup.DailyBackupTime = "99:99";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be("The daily backup time must be a time of day, written as 20:00.");
        settings.Current.Backup.DailyBackupTime.Hour.Should().Be(20);
    }

    [Fact]
    public async Task P1_T03_TheScreenShowsWhatIsInForceWhenItOpensNotWhatItSawAtStartUp()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        using var screen = Open(fixture);
        screen.Shop.Name.Should().BeEmpty();

        // Something else changes a setting - the first-run wizard, an import, a later feature.
        await settings.UpdateAsync(current => current with
        {
            Shop = current.Shop with { Name = "Nimal Hardware" },
        });

        // Opening the screen again re-reads. This is the risk P1-T03 names by name.
        screen.LoadCommand.Execute(null);

        screen.Shop.Name.Should().Be("Nimal Hardware");
    }

    [Fact]
    public async Task P1_T03_AnOpenScreenWithNoEditsFollowsAChangeMadeElsewhere()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        using var screen = Open(fixture);

        await settings.UpdateAsync(current => current with
        {
            Shop = current.Shop with { Name = "Nimal Hardware" },
        });

        screen.Shop.Name.Should().Be(
            "Nimal Hardware",
            "the screen subscribes to ISettings.Changed, so it never shows a value that is no "
            + "longer in force");
    }

    [Fact]
    public async Task P1_T03_UndoBringsBackWhatTheShopIsActuallyTradingOn()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        using var screen = Open(fixture);
        screen.Shop.Name = "A name nobody agreed to";
        screen.HasUnsavedChanges.Should().BeTrue();

        screen.RevertCommand.Execute(null);

        screen.Shop.Name.Should().BeEmpty();
        screen.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task UI_05_EscapeAsksBeforeThrowingUnsavedChangesAway()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        using var screen = Open(fixture);
        var closed = 0;
        screen.CloseRequested += (_, _) => closed++;

        screen.Shop.Name = "Half-typed";
        screen.RequestCloseCommand.Execute(null);

        closed.Should().Be(0, "it says what will happen first");
        screen.Status.Should().Contain("not saved");

        screen.RequestCloseCommand.Execute(null);
        closed.Should().Be(1, "the second press closes it");
    }

    [Fact]
    public async Task FR_10_7_ANewBackupPassphraseGoesToTheProtectedStoreAndNotIntoTheDatabase()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var passphrases = fixture.Resolve<IBackupPassphraseStore>();

        using var screen = Open(fixture);
        screen.Backup.PassphraseIsSet.Should().BeFalse();

        screen.Backup.NewPassphrase = "correct horse battery staple";
        screen.Backup.ConfirmPassphrase = "correct horse battery staple";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be("Saved. The backup passphrase has been replaced.");
        passphrases.HasPassphrase().Should().BeTrue();
        screen.Backup.PassphraseIsSet.Should().BeTrue("the screen re-read what is in force");
        screen.Backup.NewPassphrase.Should().BeEmpty("a passphrase is not left sitting in a box");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE value LIKE '%correct horse%';"))
            .Should().Be(0, "NFR-S6: it never goes into the database it protects a copy of");
    }

    [Fact]
    public async Task FR_10_7_TwoDifferentPassphrasesAreRefusedBeforeAnythingIsStored()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var passphrases = fixture.Resolve<IBackupPassphraseStore>();

        using var screen = Open(fixture);
        screen.Backup.NewPassphrase = "one thing";
        screen.Backup.ConfirmPassphrase = "another thing";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be("The two backup passphrases are not the same. Type them again.");
        passphrases.HasPassphrase().Should().BeFalse();
    }

    [Fact]
    public async Task FR_10_TheEightGroupsAreTheEightGroupsFR10Names()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        using var screen = Open(fixture);

        screen.Groups.Select(group => group.Title).Should().Equal(
            "Shop profile",
            "Financial",
            "Tax",
            "Numbering",
            "Policy",
            "Peripherals",
            "Backup",
            "Receipt");

        screen.Groups.Select(group => group.Requirement).Should().Equal(
            "FR-10.1",
            "FR-10.2",
            "FR-10.3",
            "FR-10.4",
            "FR-10.5",
            "FR-10.6",
            "FR-10.7",
            "FR-10.8");
    }

    [Fact]
    public async Task AC_17_ACashierAtTheSettingsScreenIsRefusedAndToldWhyRatherThanCrashing()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var settings = fixture.Resolve<ISettings>();

        // The screen opens and reads: the cashier's sale path reads these settings on every line,
        // so the read side must work for them (SRS FR-10.2).
        using var screen = Open(fixture);
        screen.Shop.Name.Should().BeEmpty();

        screen.Policy.ReturnWindowDays = "365";
        screen.Financial.DecimalPlaces = "3";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be(
            "Priya is signed in as cashier. ISettings.SaveAsync needs the owner.",
            "the Application layer refused, and the screen shows the refusal as a sentence "
            + "instead of falling over (SRS UI-06, NFR-S2)");

        settings.Current.Policy.ReturnWindowDays.Should().Be(
            14,
            "a refused save changes nothing, in the cache or on disk");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE key = 'policy.return_window_days';"))
            .Should().Be(0);

        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SETTING_CHANGED';"))
            .Should().Be(0, "nothing ran, so nothing was audited");
    }

    [Fact]
    public async Task AC_17_TheISettingsTheContainerHandsOutIsTheRoleDecoratedOne()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // Unlike IUserAdministration, the concrete SettingsService does keep a registration of its
        // own: FirstRunSetupService is handed it deliberately, because first run has nobody signed
        // in and writes through the internal SaveAsAsync instead. What must be true is that the
        // ISettings every screen and every use case is handed is the decorated one - and that the
        // concrete class is internal, so only the composition roots can name it at all.
        fixture.Resolve<ISettings>().Should().NotBeOfType<SettingsService>(
            "ISettings is registered decorated, so SaveAsync and UpdateAsync cannot be reached "
            + "without the role check (SRS NFR-S2, AC-17)");

        typeof(SettingsService).IsPublic.Should().BeFalse(
            "a public implementation could be constructed or resolved with nothing in front of it");
    }

    [Fact]
    public async Task AC_17_ARefusedSaveDoesNotReplaceTheShopsBackupPassphrase()
    {
        // NFR-S6, FR-11.4. The passphrase is not an app_setting row, so it is outside the settings
        // transaction, outside the audit trail and outside anything that could be rolled back or
        // reconciled afterwards. Replacing it during a save that is then refused would make every
        // existing backup permanently unrestorable, silently, under a status message that reads as
        // "nothing changed".
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var passphrases = fixture.Resolve<IBackupPassphraseStore>();

        const string ShopsPassphrase = "the passphrase every backup is encrypted under";

        using (var ownerScreen = Open(fixture))
        {
            ownerScreen.Backup.NewPassphrase = ShopsPassphrase;
            ownerScreen.Backup.ConfirmPassphrase = ShopsPassphrase;
            await ownerScreen.SaveCommand.ExecuteAsync(null);
        }

        passphrases.TryGetPassphrase().Should().Be(ShopsPassphrase, "the owner set it");

        await SignInAsCashierAsync(fixture);

        using var screen = Open(fixture);
        screen.Policy.ReturnWindowDays = "365";
        screen.Backup.NewPassphrase = "whatever the cashier typed";
        screen.Backup.ConfirmPassphrase = "whatever the cashier typed";

        await screen.SaveCommand.ExecuteAsync(null);

        screen.Status.Should().Be(
            "Priya is signed in as cashier. ISettings.SaveAsync needs the owner.",
            "the settings write is attempted first and refused first, so the screen shows that "
            + "refusal as a sentence (UI-06)");

        passphrases.TryGetPassphrase().Should().Be(
            ShopsPassphrase,
            "a refused save changes nothing at all - including the one thing no transaction could "
            + "have rolled back for it (NFR-S6)");

        // And the same call with the screen taken out of the picture entirely: hiding the button
        // is not authorisation, so the store itself has to refuse (NFR-S2, AC-17).
        var direct = () => passphrases.SetPassphrase("straight past the screen");

        direct.Should().Throw<NotAuthorisedException>(
            "IBackupPassphraseStore is registered role-decorated, exactly as ISettings is");

        passphrases.TryGetPassphrase().Should().Be(ShopsPassphrase);
    }

    /// <summary>
    /// The fixture with a cashier account created by the owner and signed in - which is what the
    /// shop looks like on any ordinary trading day.
    /// </summary>
    private static async Task<SaleFixture> SignedInAsCashierAsync()
    {
        var fixture = await SaleFixture.CreateSignedInAsync();

        try
        {
            await SignInAsCashierAsync(fixture);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Creates the shop's cashier as the signed-in owner, then signs the owner out and the cashier
    /// in - the handover that happens at the counter every morning.
    /// </summary>
    private static async Task SignInAsCashierAsync(SaleFixture fixture)
    {
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();

        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();
    }

    /// <summary>Builds the screen and opens it, which is what re-reads the settings in force.</summary>
    private static SettingsViewModel Open(SaleFixture fixture)
    {
        var screen = new SettingsViewModel(
            fixture.Resolve<ISettings>(),
            fixture.Resolve<IBackupPassphraseStore>());

        screen.LoadCommand.Execute(null);
        return screen;
    }

    private static Task<long> AuditCountAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SETTING_CHANGED';");
}

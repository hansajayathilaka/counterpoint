using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Settings;

/// <summary>
/// The headless half of the first-run wizard (P1-T03 "Do this" 5, SRS FR-10, FR-1.3): does an
/// empty database come out of it as a shop that can trade?
/// </summary>
public sealed class FirstRunSetupTests
{
    private const string OwnerPassword = "shopkeeper2026";
    private const string BackupPassphrase = "correct horse battery staple";

    [Fact]
    public async Task FR_10_TheWizardProducesAUsableSystemFromAnEmptyDatabase()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IFirstRunSetup>();

        (await setup.IsRequiredAsync()).Should().BeTrue("nothing has configured this database");

        (await setup.CompleteAsync(Request())).Should().BeTrue();

        // 1. The settings the owner chose are in force, without a reload.
        var settings = fixture.Resolve<ISettings>();
        settings.Shop.Name.Should().Be("Nimal Hardware");
        settings.Financial.DecimalPlaces.Should().Be(2);
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

        // 4. The bill series is numbered the way the owner asked.
        (await fixture.ScalarAsync("SELECT prefix FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("NH-");
        (await fixture.ScalarAsync("SELECT COUNT(*) FROM number_sequence;"))
            .Should().Be("6", "every series FR-10.4 names, plus the shift series the till needs");

        // 5. The passphrase is in the protected store, not in the database.
        fixture.Resolve<IBackupPassphraseStore>().HasPassphrase().Should().BeTrue();
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE value = '" + BackupPassphrase + "';"))
            .Should().Be(0, "NFR-S6: it never goes into the database it protects a copy of");

        // 6. And the owner can sign in - which is what "usable" finally means.
        var login = await fixture.Resolve<IAuthenticationService>()
            .LogInAsync("owner", OwnerPassword);

        login.Succeeded.Should().BeTrue(login.Message);

        (await setup.IsRequiredAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task FR_10_TheWizardIsIdempotent()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IFirstRunSetup>();

        (await setup.CompleteAsync(Request())).Should().BeTrue();

        var taxClasses = await fixture.CountAsync("SELECT COUNT(*) FROM tax_class;");
        var sequences = await fixture.CountAsync("SELECT COUNT(*) FROM number_sequence;");
        var settingRows = await fixture.CountAsync("SELECT COUNT(*) FROM app_setting;");

        var second = await setup.CompleteAsync(Request() with
        {
            Settings = SettingDefaults.Snapshot with
            {
                Shop = SettingDefaults.Shop with { Name = "Somebody Else's Shop" },
            },
        });

        second.Should().BeFalse("a database that has been set up is left alone");

        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class;")).Should().Be(taxClasses);
        (await fixture.CountAsync("SELECT COUNT(*) FROM number_sequence;")).Should().Be(sequences);
        (await fixture.CountAsync("SELECT COUNT(*) FROM app_setting;")).Should().Be(settingRows);

        fixture.Resolve<ISettings>().Shop.Name.Should().Be("Nimal Hardware");
    }

    [Fact]
    public async Task FR_10_9_TheWizardIsAudited()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.Resolve<IFirstRunSetup>().CompleteAsync(Request());

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'FIRST_RUN_COMPLETED';"))
            .Should().Be(1);

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'SETTING_CHANGED';"))
            .Should().BeGreaterThan(0, "the settings it wrote are settings changes (FR-10.9)");

        var actor = await fixture.ScalarAsync(
            """
            SELECT user_id FROM audit_log
             WHERE action = 'FIRST_RUN_COMPLETED'
             LIMIT 1;
            """);

        actor.Should().Be(
            await fixture.ScalarAsync("SELECT id FROM app_user WHERE username = 'owner';"),
            "nobody is signed in during first run, so it is filed against the owner it sets up");
    }

    [Fact]
    public async Task FR_10_AWizardThatFailsLeavesTheDatabaseUnconfigured()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IFirstRunSetup>();

        // A password the policy refuses. It is checked inside the transaction, after the settings
        // and the tax classes have been written to it.
        var run = () => setup.CompleteAsync(Request() with { OwnerPassword = "x" });

        await run.Should().ThrowAsync<InvalidOperationException>();

        (await setup.IsRequiredAsync()).Should().BeTrue(
            "the whole setup is one transaction; a failure leaves nothing behind");

        (await fixture.CountAsync("SELECT COUNT(*) FROM app_setting WHERE key = 'shop.name';"))
            .Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Standard';"))
            .Should().Be(0);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'FIRST_RUN_COMPLETED';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_10_4_TheBillNumberFormatTheWizardTakesIsTheOneTheAllocatorUses()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.Resolve<IFirstRunSetup>().CompleteAsync(Request());

        (await fixture.ScalarAsync("SELECT pattern FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be(
                SettingDefaults.YearlyNumberPattern,
                "the pattern the allocator expands comes from the setting the wizard collected");
    }

    [Fact]
    public async Task FR_10_4_TheStartingNumberTheWizardTakesIsTheCounterTheAllocatorStartsFrom()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // FirstRunSeeder has already created the SALE and SHIFT rows at next_val = 1, before the
        // wizard ever runs. An owner who asks for bills to start at 5000 must get 5000: an
        // app_setting row saying 5000 over a counter still sitting at 1 is two answers to one
        // question, and nothing ever reconciles them (FR-10.4).
        await fixture.Resolve<IFirstRunSetup>().CompleteAsync(Request() with
        {
            Settings = Request().Settings with
            {
                Numbering = SettingDefaults.Numbering with
                {
                    Bill = SettingDefaults.Numbering.Bill with
                    {
                        Prefix = "NH-",
                        StartingNumber = 5000,
                    },
                    Shift = SettingDefaults.Numbering.Shift with { StartingNumber = 300 },
                    Return = SettingDefaults.Numbering.Return with { StartingNumber = 700 },
                },
            },
        });

        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("5000", "the series the seeder created is completed, not ignored");

        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SHIFT';"))
            .Should().Be("300", "the same is true of the other series the seeder created");

        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'RETURN';"))
            .Should().Be("700", "and of the series the wizard creates from nothing");

        // And it is the number the allocator actually issues, not just a row in a table.
        var allocated = await fixture.Resolve<IDocumentNumberAllocator>()
            .AllocateAsync("SALE", new DateOnly(2026, 9, 6));

        allocated.Should().Be(
            "NH-2026-005000",
            "the first bill the shop issues is the one the owner asked for");
    }

    private static FirstRunSetupRequest Request() => new(
        SettingDefaults.Snapshot with
        {
            Shop = SettingDefaults.Shop with
            {
                Name = "Nimal Hardware",
                AddressLine1 = "42 Galle Road",
                Phone = "038-2233445",
            },
            Numbering = SettingDefaults.Numbering with
            {
                Bill = SettingDefaults.Numbering.Bill with { Prefix = "NH-" },
            },
            Backup = SettingDefaults.Backup with { LocalPath = "/srv/counterpoint/backups" },
        },
        new List<TaxClassDefinition>
        {
            new("Exempt", TaxRate.Zero),
            new("Standard", TaxRate.FromPercent(18m)),
        },
        OwnerUsername: "owner",
        OwnerPassword,
        BackupPassphrase);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Settings;

/// <summary>
/// The settings framework against a real encrypted database (SRS FR-10.1-10.9, NFR-M1).
/// </summary>
public sealed class SettingsServiceTests
{
    [Fact]
    public async Task FR_10_1_to_10_8_ASavedSettingIsPersistedAndReadBack()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        await settings.UpdateAsync(current => current with
        {
            Shop = current.Shop with
            {
                Name = "Nimal Hardware",
                Phone = "038-2233445",
                TaxRegistrationNumber = "TAX-99887766",
            },
        });

        settings.Shop.Name.Should().Be("Nimal Hardware");

        // Read back off the raw table, not off the object we just wrote.
        var stored = await fixture.ScalarAsync(
            "SELECT value FROM app_setting WHERE key = 'shop.name';");

        stored.Should().Be("Nimal Hardware", "the setting is persisted, not just remembered");

        // And a fresh load - what a restart would do - sees the same thing.
        await settings.LoadAsync();
        settings.Shop.TaxRegistrationNumber.Should().Be("TAX-99887766");
    }

    [Fact]
    public async Task FR_10_2_ChangingTheDecimalPlacesChangesRoundingWithoutARestart()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        // The very same IRoundingPolicy instance the sale path was handed at start-up.
        var rounding = fixture.Resolve<IRoundingPolicy>();

        rounding.DecimalPlaces.Should().Be(2, "Q-01: LKR at two places");
        rounding.Round(Money.FromDecimal(1.239m)).Amount.Should().Be(1.24m);

        await settings.UpdateAsync(current => current with
        {
            Financial = current.Financial with { DecimalPlaces = 3 },
        });

        rounding.DecimalPlaces.Should().Be(
            3,
            "nothing was rebuilt and nothing was restarted; the policy reads the setting");
        rounding.Round(Money.FromDecimal(1.2394m)).Amount.Should().Be(1.239m);
    }

    [Fact]
    public async Task FR_10_2_ChangingTheDecimalPlacesChangesWhatIsPrinted()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        Total(settings.Current).Should().Be(
            "2000.00",
            "the bill total prints to two decimal places while the shop says two");

        var updated = await settings.UpdateAsync(current => current with
        {
            Financial = current.Financial with { DecimalPlaces = 3 },
        });

        Total(updated).Should().Be(
            "2000.000",
            "the same bill prints to three places once the shop says three, with nothing rebuilt "
            + "and nothing restarted (FR-10.2)");
    }

    [Fact]
    public async Task FR_10_8_TheReceiptTemplateIsTheShopsWordsAndItsTaxRate()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        TextLines(settings.Current).Should().Contain("Thank you - please come again");
        LeftColumns(settings.Current).Should().Contain(
            "Tax @ 0%",
            "the default class is exempt, and the label is settings' not the renderer's (Q-02)");

        var updated = await settings.UpdateAsync(current => current with
        {
            Receipt = current.Receipt with { FooterText = "Bohoma sthuthi" },
            Tax = current.Tax with { TaxLabel = "VAT", DefaultTaxRate = TaxRate.FromPercent(15m) },
        });

        TextLines(updated).Should().Contain("Bohoma sthuthi", "FR-10.8");
        LeftColumns(updated).Should().Contain(
            "VAT @ 15%",
            "the tax label and rate come from settings, so the regime is a data decision (FR-10.3)");

        // The label was never the whole story: the shipped default is PricesIncludeTax = true, so
        // a 15% rate carves 15% out of the 2000.00 taxable value rather than adding it on top.
        // Asserting only the label is what let the specimen double-count tax at any non-zero rate.
        Total(updated).Should().Be(
            "2000.00",
            "the line prices already contain their tax, so the total is the taxable value itself "
            + "and not the taxable value plus tax again (FR-10.3)");
    }

    [Fact]
    public async Task FR_10_3_TheSpecimenCarvesTaxOutOfInclusivePricesAndAddsItToExclusiveOnes()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        // Same four lines, same 65.00 discount, same 2000.00 of value on the bill. What changes is
        // whether that 2000.00 is the gross the customer pays or the net the tax is added to.
        var inclusive = await settings.UpdateAsync(current => current with
        {
            Tax = current.Tax with
            {
                PricesIncludeTax = true,
                DefaultTaxRate = TaxRate.FromPercent(15m),
            },
        });

        Amount(inclusive, "TOTAL").Should().Be("2000.00", "the tax was already inside the prices");
        Amount(inclusive, "Taxable value").Should().Be(
            "1739.13",
            "under inclusive pricing the taxable value is the net inside the gross, not the gross");
        Amount(inclusive, "Tax @ 15%").Should().Be("260.87");

        // 1739.13 + 260.87 = 2000.00 exactly. A bill whose parts do not add up to its total is a
        // bill the shop cannot answer a customer's question about.
        (Printed(inclusive, "Taxable value") + Printed(inclusive, "Tax @ 15%"))
            .Should().Be(Printed(inclusive, "TOTAL"));

        var exclusive = await settings.UpdateAsync(current => current with
        {
            Tax = current.Tax with { PricesIncludeTax = false },
        });

        Amount(exclusive, "TOTAL").Should().Be("2300.00", "here the tax is genuinely added on top");
        Amount(exclusive, "Taxable value").Should().Be("2000.00");
        Amount(exclusive, "Tax @ 15%").Should().Be("300.00");
    }

    [Fact]
    public async Task FR_10_9_EverySettingsChangeWritesAnAuditRowWithBeforeAndAfterJson()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        await settings.UpdateAsync(current => current with
        {
            Policy = current.Policy with { ReturnWindowDays = 14 },
        });

        var baseline = await AuditCountAsync(fixture);

        await settings.UpdateAsync(current => current with
        {
            Policy = current.Policy with { ReturnWindowDays = 30 },
        });

        (await AuditCountAsync(fixture)).Should().Be(
            baseline + 1,
            "one changed setting is one audit row (FR-10.9)");

        var before = await fixture.ScalarAsync(
            """
            SELECT before_json FROM audit_log
             WHERE action = 'SETTING_CHANGED'
               AND after_json LIKE '%policy.return_window_days%'
             ORDER BY id DESC LIMIT 1;
            """);

        var after = await fixture.ScalarAsync(
            """
            SELECT after_json FROM audit_log
             WHERE action = 'SETTING_CHANGED'
               AND after_json LIKE '%policy.return_window_days%'
             ORDER BY id DESC LIMIT 1;
            """);

        before.Should().Be("""{"key":"policy.return_window_days","value":"14"}""");
        after.Should().Be("""{"key":"policy.return_window_days","value":"30"}""");

        var userId = await fixture.ScalarAsync(
            """
            SELECT user_id FROM audit_log
             WHERE action = 'SETTING_CHANGED'
             ORDER BY id DESC LIMIT 1;
            """);

        var ownerId = await fixture.ScalarAsync("SELECT id FROM app_user WHERE username = 'owner';");

        userId.Should().Be(ownerId, "the audit row names who changed it (FR-10.9)");
    }

    [Fact]
    public async Task FR_10_9_SavingAnUnchangedSnapshotWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        await settings.SaveAsync(settings.Current);
        var baseline = await AuditCountAsync(fixture);

        await settings.SaveAsync(settings.Current);

        (await AuditCountAsync(fixture)).Should().Be(
            baseline,
            "an audit trail full of 'the owner pressed Save' is one nobody reads");
    }

    [Fact]
    public async Task FR_10_9_AFailedWriteLeavesNoRowNoAuditRowAndNoPoisonedCache()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();
        var store = (ISettingStore)new FailingSettingStore(fixture.Resolve<ISettingStore>());

        var failing = new SettingsService(
            store,
            fixture.Resolve<IUnitOfWork>(),
            fixture.Resolve<IAuditTrail>(),
            fixture.Resolve<Application.Security.ISession>(),
            fixture.Resolve<Application.Abstractions.Security.IBackupPassphraseStore>(),
            fixture.Resolve<INumberSequenceConfiguration>(),
            fixture.Resolve<TimeProvider>());

        await failing.LoadAsync();
        var baseline = await AuditCountAsync(fixture);

        var save = () => failing.UpdateAsync(current => current with
        {
            Shop = current.Shop with { Name = "Never Committed Hardware" },
        });

        await save.Should().ThrowAsync<InvalidOperationException>();

        failing.Shop.Name.Should().NotBe(
            "Never Committed Hardware",
            "the cache is published after the commit, so a failed write cannot outrun it");

        (await AuditCountAsync(fixture)).Should().Be(
            baseline,
            "the audit row is written inside the same transaction and rolls back with it");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE value = 'Never Committed Hardware';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_10_1_to_10_8_TheSettingsAreSafeToReadFromManyThreadsAtOnce()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        const int Reads = 20_000;

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < Reads; i++)
                {
                    // A snapshot is one immutable object: every read is internally consistent,
                    // whatever a writer is doing at the same moment. A torn read here would show
                    // up as a null group or an empty currency code.
                    var current = settings.Current;
                    current.Financial.CurrencyCode.Should().NotBeNullOrEmpty();
                    current.Numbering.Bill.Prefix.Should().NotBeNull();
                    current.Receipt.FooterText.Should().NotBeNull();
                }
            })).ToArray();

        for (var days = 1; days <= 8; days++)
        {
            var window = days;
            await settings.UpdateAsync(current => current with
            {
                Policy = current.Policy with { ReturnWindowDays = window },
            });
        }

        await Task.WhenAll(readers);

        settings.Policy.ReturnWindowDays.Should().Be(8, "the last write is the one in force");
    }

    [Fact]
    public async Task FR_10_4_EditingABillPrefixUpdatesTheSeriesButNeverItsCounter()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        var counterBefore = await fixture.ScalarAsync(
            "SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';");

        await settings.UpdateAsync(current => current with
        {
            Numbering = current.Numbering with
            {
                Bill = current.Numbering.Bill with { Prefix = "BILL-", StartingNumber = 9000 },
            },
        });

        (await fixture.ScalarAsync("SELECT prefix FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("BILL-", "the prefix is the owner's to change (FR-10.4)");

        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be(
                counterBefore,
                "a counter that has issued numbers is never moved - that is what keeps the series "
                + "gapless (CLAUDE.md invariant 4, AC-19)");
    }

    [Fact]
    public async Task FR_10_TheFrameworkLeavesTheSecurityPolicyRowsAlone()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var before = await fixture.ScalarAsync(
            "SELECT value FROM app_setting WHERE key = 'security.login.max_failed_attempts';");

        before.Should().NotBeNull("P1-T02 records the lockout policy in the same table");

        await fixture.Resolve<ISettings>().UpdateAsync(current => current with
        {
            Receipt = current.Receipt with { FooterText = "Come again" },
        });

        (await fixture.ScalarAsync(
            "SELECT value FROM app_setting WHERE key = 'security.login.max_failed_attempts';"))
            .Should().Be(before, "this framework only ever writes the keys it owns");
    }

    [Fact]
    public async Task FR_10_1_to_10_8_TheFirstSaveMaterialisesEveryDefaultRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        await settings.SaveAsync(settings.Current);

        var expected = SettingsSerializer.ToRows(SettingDefaults.Snapshot).Count;

        var persisted = await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE key NOT LIKE 'security.%';");

        persisted.Should().Be(
            expected,
            "every FR-10.1-10.8 setting is present and persisted, not merely defaulted in memory");
    }

    [Fact]
    public async Task FR_10_ARefusedValueNeverReachesTheDatabase()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var settings = fixture.Resolve<ISettings>();

        var save = () => settings.UpdateAsync(current => current with
        {
            Financial = current.Financial with { DecimalPlaces = 9 },
        });

        await save.Should().ThrowAsync<ArgumentException>(
            "money is stored scaled by 10 000, so nine decimal places do not exist");

        settings.Financial.DecimalPlaces.Should().Be(2);
    }

    /// <summary>The specimen bill's total, as it would be printed under these settings.</summary>
    private static string Total(SettingsSnapshot settings) => Amount(settings, "TOTAL");

    /// <summary>The amount printed against a named row of the specimen bill.</summary>
    private static string Amount(SettingsSnapshot settings, string label) =>
        SpecimenReceipt.Build(settings).Nodes
            .OfType<ReceiptNode.Columns>()
            .Single(node => node.Left == label)
            .Right;

    /// <summary>That same printed amount as a number, so printed rows can be added up.</summary>
    private static decimal Printed(SettingsSnapshot settings, string label) =>
        decimal.Parse(Amount(settings, label), CultureInfo.InvariantCulture);

    private static IEnumerable<string> LeftColumns(SettingsSnapshot settings) =>
        SpecimenReceipt.Build(settings).Nodes
            .OfType<ReceiptNode.Columns>()
            .Select(node => node.Left);

    private static IEnumerable<string> TextLines(SettingsSnapshot settings) =>
        SpecimenReceipt.Build(settings).Nodes
            .OfType<ReceiptNode.TextLine>()
            .Select(node => node.Text);

    private static Task<long> AuditCountAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SETTING_CHANGED';");

    /// <summary>
    /// A store that reads normally and refuses to write. The failure lands inside the
    /// transaction, which is exactly where a real one would.
    /// </summary>
    private sealed class FailingSettingStore : ISettingStore
    {
        private readonly ISettingStore _inner;

        internal FailingSettingStore(ISettingStore inner) => _inner = inner;

        public Task<IReadOnlyDictionary<string, StoredSetting>> LoadAllAsync(
            CancellationToken cancellationToken = default) => _inner.LoadAllAsync(cancellationToken);

        public Task WriteAsync(
            IReadOnlyList<SettingWrite> writes,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Refusing to write {writes.Count} settings."));
    }
}

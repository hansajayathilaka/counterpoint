using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Settings;

/// <summary>
/// The complete default set and the mapping that stores it (SRS FR-10.1-10.8).
///
/// Every later Phase 1 feature reads its limits from <see cref="SettingDefaults"/>, so a setting
/// that is missing here is a feature with a number written into its own code later.
/// </summary>
public sealed class SettingDefaultsTests
{
    /// <summary>The <c>value_type</c> tokens <c>app_setting</c>'s CHECK constraint allows.</summary>
    private static readonly string[] AllowedValueTypes = ["STRING", "INT", "MONEY", "BOOL", "JSON"];

    [Fact]
    public void FR_10_1_to_10_8_EveryKeyIsPersistedExactlyOnce()
    {
        var rows = SettingsSerializer.ToRows(SettingDefaults.Snapshot);

        rows.Select(row => row.Key).Should().OnlyHaveUniqueItems(
            "one setting is one row; two rows for one key is a setting with two answers");

        var declared = DeclaredKeys();
        var persisted = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

        // setup.completed_at is written by the first-run wizard, not by the snapshot, so it is
        // the one declared key that is deliberately absent from the rows.
        declared.Remove(SettingKeys.SetupCompletedAt);

        persisted.Should().BeEquivalentTo(
            declared,
            "SettingKeys and SettingsSerializer must agree: a key nobody writes is a setting the "
            + "owner can never change, and a row under a key nobody declared cannot be found again");
    }

    [Fact]
    public void FR_10_1_to_10_8_EveryGroupContributesSettings()
    {
        var rows = SettingsSerializer.ToRows(SettingDefaults.Snapshot);

        var groups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shop."] = "FR-10.1 shop profile",
            ["financial."] = "FR-10.2 financial",
            ["tax."] = "FR-10.3 tax",
            ["numbering."] = "FR-10.4 numbering",
            ["policy."] = "FR-10.5 policy",
            ["peripheral."] = "FR-10.6 peripherals",
            ["backup."] = "FR-10.7 backup",
            ["receipt."] = "FR-10.8 receipt template",
            ["label."] = "FR-2.10, FR-2.12 label layout",
        };

        foreach (var (prefix, requirement) in groups)
        {
            rows.Should().Contain(
                row => row.Key.StartsWith(prefix, StringComparison.Ordinal),
                "{0} must have settings", requirement);
        }
    }

    [Fact]
    public void FR_10_1_to_10_8_EveryRowUsesAValueTypeTheSchemaAllows()
    {
        var rows = SettingsSerializer.ToRows(SettingDefaults.Snapshot);

        rows.Should().AllSatisfy(row => AllowedValueTypes.Should().Contain(
            row.ValueType,
            "app_setting.value_type has a CHECK constraint and a row outside it is rejected by "
            + "the database, not by us"));
    }

    [Fact]
    public void FR_10_2_TheShopTradesInRupeesAtTwoDecimalPlaces()
    {
        var financial = SettingDefaults.Financial;

        financial.CurrencyCode.Should().Be("LKR", "Q-01");
        financial.DecimalPlaces.Should().Be(2, "Q-01");
        financial.RoundingRule.Should().Be(
            RoundingRule.HalfAwayFromZero,
            "half away from zero keeps a return the exact mirror of the sale it reverses");
    }

    [Fact]
    public void FR_10_3_TaxDefaultsToASingleExemptClassAndInclusivePricing()
    {
        var tax = SettingDefaults.Tax;

        tax.DefaultTaxRate.Should().Be(
            TaxRate.Zero,
            "Q-02 defers the regime; a rate invented here would be a wrong number on a bill");
        tax.DefaultTaxClassName.Should().Be("Exempt");
        tax.PricesIncludeTax.Should().BeTrue("the shop's shelf prices are what the customer pays");
        SettingDefaults.Shop.TaxRegistrationNumber.Should().BeEmpty(
            "the shop has no registration number until it says it has");
    }

    /// <summary>
    /// Q-16 is unanswered. Until it is answered, the numbering defaults must reproduce exactly
    /// what <c>FirstRunSeeder</c> already seeds, so that taking the series over changes no
    /// existing bill number and no existing test.
    /// </summary>
    [Fact]
    public void FR_10_4_TheNumberingDefaultsReproduceTheSeededSeries()
    {
        var numbering = SettingDefaults.Numbering;

        numbering.Bill.Should().Be(
            new DocumentNumbering("INV-", "{prefix}{yyyy}-{n:000000}", 1),
            "P0-T06 seeds SALE as INV-{yyyy}-{n:000000} starting at 1");
        numbering.Shift.Should().Be(
            new DocumentNumbering("SH-", "{prefix}{n:000000}", 1),
            "a shift is not numbered by year");

        numbering.BySequence.Select(pair => pair.Key).Should().BeEquivalentTo(
            ["SALE", "RETURN", "CREDIT_NOTE", "GRN", "PO", "SHIFT"],
            "every doc_type the series covers must be one number_sequence's CHECK allows");

        numbering.BySequence.Should().AllSatisfy(pair => pair.Value.StartingNumber.Should().Be(
            1,
            "the allocator returns the value before the increment, so the first document is 1"));
    }

    [Fact]
    public void FR_10_5_ThePolicyDefaultsAreTheShopsAnswers()
    {
        var policy = SettingDefaults.Policy;

        policy.AllowUnlinkedReturns.Should().BeFalse("Q-03: a return references a previous bill");
        policy.NegativeStock.Should().Be(NegativeStockPolicy.Allow, "Q-11");
        policy.MaxLineDiscountRate.Should().Be(
            Percentage.OneHundredPercent,
            "Q-12 is 'not for now': the limit exists and restricts nothing");
        policy.MaxBillDiscountRate.Should().Be(Percentage.OneHundredPercent, "Q-12");
        policy.CashRefundLimit.Should().Be(Money.Zero, "zero means no limit");
        policy.ReturnWindowDays.Should().Be(14);
    }

    [Fact]
    public void FR_10_7_TheOffSiteTargetIsTheOneTheShopChose()
    {
        SettingDefaults.Backup.CloudTarget.Should().Be(CloudBackupTarget.GoogleDrive, "Q-D");
        SettingDefaults.Backup.PassphraseIsSet.Should().BeFalse(
            "the passphrase is not a settings row at all; it is read from the protected store");

        SettingsSerializer.ToRows(SettingDefaults.Snapshot)
            .Should().NotContain(
                row => row.Key.Contains("passphrase", StringComparison.OrdinalIgnoreCase),
                "app_setting lives inside the database the backup is a copy of (NFR-S6)");
    }

    [Fact]
    public void FR_10_1_to_10_8_ASettingSurvivesTheRoundTripToRowsAndBack()
    {
        var edited = Edited();

        var stored = SettingsSerializer.ToRows(edited).ToDictionary(
            row => row.Key,
            row => new StoredSetting(row.Value, row.ValueType),
            StringComparer.Ordinal);

        var restored = SettingsSerializer.FromRows(stored, SettingDefaults.Snapshot);

        restored.Should().Be(
            edited,
            "every FR-10.1-10.8 setting must be persisted and read back exactly, or an owner's "
            + "change silently reverts on the next start");
    }

    [Fact]
    public void FR_10_1_to_10_8_AnEmptyTableReadsAsTheDefaults()
    {
        var restored = SettingsSerializer.FromRows(
            new Dictionary<string, StoredSetting>(StringComparer.Ordinal),
            SettingDefaults.Snapshot);

        restored.Should().Be(
            SettingDefaults.Snapshot,
            "a shop that has never opened the settings screen trades on the defaults");
    }

    [Fact]
    public void FR_10_1_to_10_8_ACorruptValueFallsBackRatherThanStoppingTheTill()
    {
        var corrupt = new Dictionary<string, StoredSetting>(StringComparer.Ordinal)
        {
            [SettingKeys.FinancialDecimalPlaces] = new("not a number", "INT"),
            [SettingKeys.FinancialRoundingRule] = new("SOMETHING_ELSE", "STRING"),
            [SettingKeys.PolicyNegativeStock] = new(string.Empty, "STRING"),
            [SettingKeys.BackupDailyTime] = new("half past nine", "STRING"),
            [SettingKeys.TaxDefaultRate] = new("-1", "INT"),
        };

        var restored = SettingsSerializer.FromRows(corrupt, SettingDefaults.Snapshot);

        restored.Financial.DecimalPlaces.Should().Be(2);
        restored.Financial.RoundingRule.Should().Be(RoundingRule.HalfAwayFromZero);
        restored.Policy.NegativeStock.Should().Be(NegativeStockPolicy.Allow);
        restored.Backup.DailyBackupTime.Should().Be(new TimeOnly(20, 0));
        restored.Tax.DefaultTaxRate.Should().Be(
            TaxRate.Zero,
            "a negative tax rate is not a tax rate; the till degrades rather than throwing "
            + "(CLAUDE.md invariant 7)");
    }

    [Fact]
    public void FR_10_2_MoneySettingsAreStoredAsTheScaledInteger()
    {
        var withLimit = SettingDefaults.Snapshot with
        {
            Policy = SettingDefaults.Policy with { CashRefundLimit = Money.FromDecimal(2500m) },
        };

        var row = SettingsSerializer.ToRows(withLimit)
            .Single(candidate => candidate.Key == SettingKeys.PolicyCashRefundLimit);

        row.ValueType.Should().Be("MONEY");
        row.Value.Should().Be(
            "25000000",
            "money is an INTEGER scaled by 10 000, never a floating type (CLAUDE.md invariant 1)");
    }

    [Fact]
    public void FR_10_5_RatesAreStoredAsTheScaledFraction()
    {
        var withCap = SettingDefaults.Snapshot with
        {
            Policy = SettingDefaults.Policy with { MaxLineDiscountRate = Percentage.FromPercent(15m) },
        };

        var row = SettingsSerializer.ToRows(withCap)
            .Single(candidate => candidate.Key == SettingKeys.PolicyMaxLineDiscountRate);

        row.Value.Should().Be("1500", "a rate is the fraction scaled by 10 000: 1500 is 15%");
    }

    /// <summary>A snapshot in which every persisted field differs from its default.</summary>
    private static SettingsSnapshot Edited() => new(
        new ShopProfileSettings(
            "Nimal Hardware",
            "42 Galle Road",
            "Panadura",
            "038-2233445",
            "shop@example.lk",
            "TAX-99887766",
            "/var/lib/counterpoint/logo.png"),
        new FinancialSettings(
            "INR",
            "#",
            CurrencySymbolPosition.After,
            DecimalPlaces: 3,
            RoundingRule.HalfToEven,
            QuantityDecimalPlaces: 4),
        new TaxSettings(
            PricesIncludeTax: false,
            "Standard",
            TaxRate.FromPercent(18m),
            "GST"),
        new NumberingSettings(
            new DocumentNumbering("B-", "{prefix}{n:0000}", 500),
            new DocumentNumbering("R-", "{prefix}{n:0000}", 7),
            new DocumentNumbering("C-", "{prefix}{n:0000}", 8),
            new DocumentNumbering("G-", "{prefix}{n:0000}", 9),
            new DocumentNumbering("P-", "{prefix}{n:0000}", 10),
            new DocumentNumbering("S-", "{prefix}{n:0000}", 11)),
        new PolicySettings(
            ReturnWindowDays: 30,
            AllowUnlinkedReturns: true,
            RefundMethod.CreditNote,
            Money.FromDecimal(5000m),
            Percentage.FromPercent(5m),
            Percentage.FromPercent(10m),
            NegativeStockPolicy.Block,
            Percentage.FromPercent(2.5m),
            CombineRepeatScans: false),
        new PeripheralSettings(
            "EPSON TM-T82",
            PaperWidthMm: 58,
            ReceiptCopies: 2,
            "Zebra GK420",
            OpenDrawerOnCashSale: false,
            DrawerKickPin: 5,
            ScannerSuffix.Tab,
            ScannerMinimumLength: 6,
            ScaleEnabled: true,
            "COM3",
            ScaleBaudRate: 19200),
        new BackupSettings(
            new TimeOnly(21, 30),
            BackupOnShiftClose: false,
            "/srv/backups",
            "/media/usb",
            CloudBackupTarget.None,
            "owner@example.lk",
            RetentionDays: 90,
            RetentionCopies: 30,

            // Not persisted, so the round trip has to see the fallback's value here. Matching the
            // default keeps the comparison honest rather than hiding a lost field.
            PassphraseIsSet: false),
        new ReceiptSettings(
            "Quality tools since 1998",
            "See you soon",
            "No returns on cut cable.",
            ShowLogo: true,
            ShowBillBarcode: false,
            ShowCashierName: false,
            ShowCustomerName: false,
            ShowItemAndUnitCount: false,
            ShowTaxSummary: false,
            ShowTaxableValue: false,
            ShowTaxRegistrationNumber: false,
            TemplateText: "TEXT|C|1|1|{{ shop.name }}"),
        new LabelSettings(
            WidthMm: 50,
            HeightMm: 25,
            GapMm: 3,
            ShowProductName: false,
            ShowCode: false,
            ShowBarcode: false,
            ShowUnit: false,
            ShowPrice: false,
            DefaultQuantityPerLabel: 5));

    /// <summary>Every key constant declared on <see cref="SettingKeys"/>.</summary>
    private static HashSet<string> DeclaredKeys() =>
        typeof(SettingKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field is { IsLiteral: true, FieldType: { } type } && type == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
}

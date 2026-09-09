using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The template-driven bridge from a completed bill to a byte stream (SRS FR-7.1, FR-7.3,
/// FR-7.4, FR-7.5, FR-7.6, FR-7.7, NFR-M1).
/// </summary>
public sealed class EscPosSaleReceiptRendererTests
{
    private const long Pieces = 1;

    [Fact]
    public void FR_7_1_TheBillNumberAndTheTotalReachThePaper()
    {
        var bytes = Render(Receipt("CASH"));

        var lines = EscPosDump.PrintedLines(bytes);

        lines.Should().Contain(line => line.Contains("INV-2026-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Galvanised bolt M8", StringComparison.Ordinal));
        lines.Should().Contain(line => line.TrimEnd().EndsWith("25.00", StringComparison.Ordinal));
    }

    [Fact]
    public void FR_7_4_TheBillNumberIsAlsoEncodedAsABarcodeSoAReturnCanScanTheReceipt()
    {
        var bytes = Render(Receipt("CASH"));

        // GS k 73 (Code 128), then the length-prefixed data.
        var barcode = FindSequence(bytes, [0x1D, 0x6B, 73]);

        barcode.Should().BeGreaterThan(-1, "the bill number prints as a Code 128 symbol");
    }

    [Fact]
    public void FR_7_7_TheDrawerOpensForCashAndOnlyForCash()
    {
        // ESC p 0 25 250
        var kick = new byte[] { 0x1B, 0x70, 0x00, 25, 250 };

        FindSequence(Render(Receipt("CASH")), kick)
            .Should().BeGreaterThan(-1, "a cash tender opens the drawer");

        FindSequence(Render(Receipt("CARD")), kick)
            .Should().Be(-1, "a card sale that popped the drawer would be a reconciliation problem");
    }

    [Fact]
    public void PRT_05_TheReceiptEndsWithACut()
    {
        var bytes = Render(Receipt("CASH"));

        // GS V 1 - partial cut, the default capability (PrinterCapabilities.CutMode).
        FindSequence(bytes, [0x1D, 0x56, 1]).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void FR_7_3_ChangingTheShopNameFooterOrPolicyTextChangesTheRenderedByteStreamWithNoRebuild()
    {
        var settings = new FixedSettings();
        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            NullLogger<EscPosSaleReceiptRenderer>.Instance);

        var before = renderer.Render(Receipt("CASH"));

        // Nothing recompiled, nothing reconstructed - the same settings object, the same
        // renderer instance, a new snapshot published exactly as SettingsService.SaveAsync does.
        settings.Set(settings.Current with
        {
            Shop = settings.Current.Shop with { Name = "Nimal Hardware" },
            Receipt = settings.Current.Receipt with
            {
                FooterText = "Come again soon!",
                PolicyText = "No returns on cut cable, ever.",
            },
        });

        var after = renderer.Render(Receipt("CASH"));

        after.Should().NotEqual(before, "a settings change must change the printed bytes with no rebuild");

        var lines = EscPosDump.PrintedLines(after);
        lines.Should().Contain(line => line.Contains("Nimal Hardware", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Come again soon!", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("No returns on cut cable, ever.", StringComparison.Ordinal));
    }

    [Fact]
    public void FR_7_3_ChangingTheTemplateTextItselfChangesTheRenderedByteStreamWithNoRebuild()
    {
        var settings = new FixedSettings();
        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            NullLogger<EscPosSaleReceiptRenderer>.Instance);

        var before = renderer.Render(Receipt("CASH"));

        settings.Set(settings.Current with
        {
            Receipt = settings.Current.Receipt with { TemplateText = "TEXT|C|1|1|A whole new layout" },
        });

        var after = renderer.Render(Receipt("CASH"));

        after.Should().NotEqual(before);
        EscPosDump.PrintedLines(after).Should().ContainSingle(
            line => line.Contains("A whole new layout", StringComparison.Ordinal));
    }

    [Fact]
    public void FR_7_6_AReprintRendersDuplicate()
    {
        var original = Render(Receipt("CASH"), isDuplicate: false);
        var reprint = Render(Receipt("CASH"), isDuplicate: true);

        EscPosDump.PrintedLines(original).Should().NotContain(
            line => line.Contains("DUPLICATE", StringComparison.Ordinal));
        EscPosDump.PrintedLines(reprint).Should().Contain(
            line => line.Contains("DUPLICATE", StringComparison.Ordinal),
            "PRT-08: reprints must carry a DUPLICATE line");
    }

    [Fact]
    public void ABrokenOwnerTemplateFallsBackToTheDefaultRatherThanBlockingTheSale()
    {
        var settings = new FixedSettings(SettingDefaults.Snapshot with
        {
            Receipt = SettingDefaults.Receipt with { TemplateText = "{{ this does not parse" },
        });

        var logger = new RecordingLogger<EscPosSaleReceiptRenderer>();
        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            logger);

        var act = () => renderer.Render(Receipt("CASH"));

        act.Should().NotThrow("a broken owner-edited template must never stop a bill printing");
        logger.Entries.Should().Contain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void ABrokenOwnerTemplateThatParsesButEmitsAnOutOfRangeDirectiveFallsBackToTheDefault()
    {
        // A template that is valid Scriban syntax but, once interpolated, hands
        // EscPos.Barcode more than the 255 data bytes GS k can carry - reachable through an
        // ordinary owner edit, not a syntax mistake ReceiptTemplateException would catch.
        var settings = new FixedSettings(SettingDefaults.Snapshot with
        {
            Receipt = SettingDefaults.Receipt with
            {
                TemplateText = "BARCODE|{{ policy_text }}",
                PolicyText = new string('9', 300),
            },
        });

        var logger = new RecordingLogger<EscPosSaleReceiptRenderer>();
        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            logger);

        var act = () => renderer.Render(Receipt("CASH"));

        act.Should().NotThrow(
            "a directive-parsing or ESC/POS-writer failure from an owner-edited template must "
            + "degrade to the default, exactly like a Scriban syntax error");
        logger.Entries.Should().Contain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void WhenTheShippedDefaultTemplateItselfProducesAnOutOfRangeDirectiveItRethrowsRatherThanLoopingForever()
    {
        // The owner has not edited the template at all - TemplateText is blank, so the effective
        // template is the shipped default, which embeds the bill number in a BARCODE| line
        // (SRS FR-7.4). A bill number long enough to bust GS k's 255-byte limit means the
        // *default* itself cannot render - there is nothing left to fall back to, so this must
        // propagate rather than recurse into RunPipeline(default, ...) a second time.
        var settings = new FixedSettings(SettingDefaults.Snapshot with
        {
            Receipt = SettingDefaults.Receipt with { TemplateText = string.Empty },
        });

        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            NullLogger<EscPosSaleReceiptRenderer>.Instance);

        var act = () => renderer.Render(Receipt("CASH", billNumber: new string('1', 300)));

        act.Should().Throw<ArgumentException>(
            "a broken shipped default is a codebase bug, not an owner's edit, so there is no "
            + "safe fallback left and this must not be swallowed");
    }

    [Fact]
    public Task Srs_10_1_TheDefaultTemplateRendersToTheCommittedByteStream()
    {
        var settings = new FixedSettings();
        var renderer = new EscPosSaleReceiptRenderer(
            new EscPosRenderer(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2),
            settings,
            NullLogger<EscPosSaleReceiptRenderer>.Instance);

        var bytes = renderer.Render(Receipt("CASH"));

        return Verifier.Verify(EscPosDump.Describe(bytes)).UseDirectory("Snapshots");
    }

    private static byte[] Render(SaleReceipt receipt, bool isDuplicate = false) =>
        new EscPosSaleReceiptRenderer(
                new EscPosRenderer(),
                new HalfAwayFromZeroRounding(decimalPlaces: 2),
                new FixedSettings(),
                NullLogger<EscPosSaleReceiptRenderer>.Instance)
            .Render(receipt, isDuplicate);

    private static SaleReceipt Receipt(string tenderType, string billNumber = "INV-2026-000001") => new(
        billNumber,
        new DateTimeOffset(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5)),
        [
            new SaleReceiptLine(
                "Galvanised bolt M8",
                Quantity.FromDecimal(2m, Pieces),
                "pc",
                Money.FromDecimal(12.50m),
                Money.FromDecimal(25.00m)),
        ],
        Money.FromDecimal(25.00m),
        Money.Zero,
        Money.FromDecimal(25.00m),
        Money.Zero,
        Money.FromDecimal(25.00m),
        [new SaleReceiptTender(tenderType, Money.FromDecimal(25.00m))],
        Money.Zero,
        [],
        "Kamal",
        "Walk-in",
        IsTradeCustomer: false);

    /// <summary>Index of the first occurrence of <paramref name="needle"/>, or -1.</summary>
    private static int FindSequence(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (Enumerable.Range(0, needle.Length).All(i => haystack[start + i] == needle[i]))
            {
                return start;
            }
        }

        return -1;
    }
}

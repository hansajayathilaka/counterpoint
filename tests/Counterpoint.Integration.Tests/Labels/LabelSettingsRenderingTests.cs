using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Labels;

/// <summary>
/// The label's size and content are configurable through the real settings store, and the change
/// reaches the rendered TSPL byte stream with nothing rebuilt (SRS FR-2.10, FR-2.12, task P1-T12
/// "Done when").
/// </summary>
/// <remarks>
/// <c>SettingDefaultsTests</c> (Counterpoint.Domain.Tests) already proves the <c>label.*</c>
/// group round-trips through <c>SettingsSerializer</c> - that only proves persistence. This
/// proves the other half: that <see cref="TsplLabelRenderer"/>, reached through the same
/// <see cref="ILabelPrintService"/> the owner's print button calls, actually reads
/// <c>ISettings.Current.Label</c> on every print rather than a value captured at start-up.
/// </remarks>
public sealed class LabelSettingsRenderingTests
{
    [Fact]
    public async Task FR_2_10_ChangingLabelSizeAndContentInSettingsChangesTheRenderedByteStreamWithNoRebuild()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        var productId = await products.CreateAsync(new SaveProductCommand(
            "LBL-SET-1",
            "Heavy duty bench vice 6 inch",
            null,
            null,
            null,
            pieceId,
            ProductType.Standard,
            exemptId,
            null,
            false,
            null,
            null,
            null));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("LBL-SET-1-A", EmptyAttributes, Money.FromDecimal(125m)));

        var labelPrinting = fixture.Resolve<ILabelPrintService>();
        var settings = fixture.Resolve<ISettings>();

        // The shipped default (SettingDefaults.Label): 40 mm wide, barcode on.
        settings.Label.WidthMm.Should().Be(40);
        settings.Label.ShowBarcode.Should().BeTrue();

        var beforeOutcome = await labelPrinting.PrintAsync(
            [new LabelPrintRequestItem(variantId, QuantityPerLabel: 1)]);
        beforeOutcome.Succeeded.Should().BeTrue(beforeOutcome.FailureReason ?? string.Empty);

        var before = await ReadLatestLabelFileAsync(fixture);
        before.Should().Contain("SIZE 40 mm,30 mm", "the default width/height, before any change");
        before.Should().Contain("BARCODE", "the default has the barcode switched on");

        // The owner changes the label's size and switches the barcode off - the same
        // ISettings.UpdateAsync path the settings screen calls, over the real SQLite file.
        await settings.UpdateAsync(current => current with
        {
            Label = current.Label with
            {
                WidthMm = 58,
                HeightMm = 40,
                ShowBarcode = false,
            },
        });

        // Read back off the raw table, not off the object just written, exactly as
        // SettingsServiceTests does - the point is that the change was actually persisted, not
        // only cached in the ISettings instance this test happens to hold.
        (await fixture.ScalarAsync("SELECT value FROM app_setting WHERE key = 'label.width_mm';"))
            .Should().Be("58");
        (await fixture.ScalarAsync("SELECT value FROM app_setting WHERE key = 'label.show_barcode';"))
            .Should().Be("false");

        // No restart, no new fixture, no rebuild of the container: the very same
        // ILabelPrintService instance is asked to print again.
        var afterOutcome = await labelPrinting.PrintAsync(
            [new LabelPrintRequestItem(variantId, QuantityPerLabel: 1)]);
        afterOutcome.Succeeded.Should().BeTrue(afterOutcome.FailureReason ?? string.Empty);

        var after = await ReadLatestLabelFileAsync(fixture);
        after.Should().Contain("SIZE 58 mm,40 mm", "the SIZE command now reflects the owner's new width and height");
        after.Should().NotContain(
            "BARCODE",
            "the BARCODE command is gone entirely, not merely reordered, once ShowBarcode is off");

        // What did not change stays: the product's own name is still on the label either way.
        after.Should().Contain("Heavy duty bench vice 6 inch");
    }

    /// <summary>The most recently written <c>*.bin</c> label file, decoded as text (TSPL is ASCII).</summary>
    private static async Task<string> ReadLatestLabelFileAsync(SaleFixture fixture)
    {
        var path = Directory.GetFiles(fixture.LabelDirectory, "*.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();

        return Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> EmptyAttributes = [];
}

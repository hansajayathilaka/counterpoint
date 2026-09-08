using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Labels;

/// <summary>
/// The owner's shelf-label print run, end to end through the real catalogue, the real settings
/// store and the real (file) label printer (SRS FR-2.10, FR-2.12, task P1-T12 "Done when").
/// </summary>
/// <remarks>
/// <see cref="Counterpoint.Devices.Labels.TsplLabelRendererTests"/> already snapshot-tests the
/// byte stream for one product in isolation; these tests exercise the orchestration
/// <see cref="LabelPrintService"/> adds on top - catalogue resolution, the batch size and the
/// owner-only gate - which only a real database and the real DI wiring can prove.
/// </remarks>
public sealed class LabelPrintServiceTests
{
    [Fact]
    public async Task FR_2_12_ABatchOf50ProductsRendersAndPrintsInOneRunWithoutTruncationOrACap()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await ReferenceDataAsync(fixture);

        const int Count = 50;
        var items = new List<LabelPrintRequestItem>(Count);

        for (var i = 1; i <= Count; i++)
        {
            var code = string.Create(CultureInfo.InvariantCulture, $"LBL-{i:000}");

            var productId = await products.CreateAsync(new SaveProductCommand(
                code,
                "Label test product " + i.ToString(CultureInfo.InvariantCulture),
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
                null,

                // The 50 names differ only by a trailing number, so FR-2.24's fuzzy
                // near-duplicate warning correctly fires from the low teens onward - this test is
                // about the label batch, not that workflow, so it confirms through the same flag
                // a bulk import would use.
                ConfirmDuplicate: true));

            var variantId = await products.CreateVariantAsync(
                productId,
                new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(10m + i)));

            items.Add(new LabelPrintRequestItem(variantId, QuantityPerLabel: 1));
        }

        var labelPrinting = fixture.Resolve<ILabelPrintService>();
        var outcome = await labelPrinting.PrintAsync(items);

        outcome.Succeeded.Should().BeTrue(outcome.FailureReason ?? string.Empty);

        var document = await ReadLatestLabelFileAsync(fixture);

        // One CLS starts every label (TsplLabelRenderer.WriteLabel) and one PRINT ends it: if
        // either count is short of 50, an item was silently dropped somewhere between the
        // request list and the byte stream, which is exactly what a hidden cap would look like.
        CountOccurrences(document, "CLS\r\n").Should().Be(
            Count, "one CLS per label - the whole batch reached the renderer, not a prefix of it");
        CountOccurrences(document, "PRINT 1,1\r\n").Should().Be(
            Count, "one PRINT per label, so the printer actually emits all 50, not just the buffer's last one");

        document.Should().Contain(
            "LBL-050",
            "the 50th product's code is present verbatim - nothing truncated the tail of the batch");
    }

    [Fact]
    public async Task AC_17_ACashierIsRefusedByTheServiceItself()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await ReferenceDataAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            "LBL-AUTH",
            "Label auth test",
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
            new SaveProductVariantCommand("LBL-AUTH-A", EmptyAttributes, Money.FromDecimal(10m)));

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var labelPrinting = fixture.Resolve<ILabelPrintService>();
        var print = () => labelPrinting.PrintAsync([new LabelPrintRequestItem(variantId, QuantityPerLabel: 1)]);

        // A shelf label carries the retail price the owner sets (SRS §3.3 ROLE-2, NFR-S2,
        // AC-17): refused by the Application layer itself, with no screen and no override
        // involved, the same shape CatalogueAuthorisationTests and BulkPriceUpdateServiceTests
        // prove for the other owner-only catalogue services.
        await print.Should().ThrowAsync<NotAuthorisedException>();

        Directory.Exists(fixture.LabelDirectory).Should().BeFalse(
            "the request never reached the renderer or the printer, so no label file exists");
    }

    private static async Task<(IProductMaintenance Products, long PieceId, long ExemptId)> ReferenceDataAsync(
        SaleFixture fixture)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (products, pieceId, exemptId);
    }

    /// <summary>The most recently written <c>*.bin</c> label file, decoded as text (TSPL is ASCII).</summary>
    private static async Task<string> ReadLatestLabelFileAsync(SaleFixture fixture)
    {
        var path = Directory.GetFiles(fixture.LabelDirectory, "*.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();

        return Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static readonly Dictionary<string, string> EmptyAttributes = [];
}

using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Devices.Labels;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Labels;

/// <summary>
/// The TSPL byte stream for a known product, locked down (SRS FR-2.10, FR-2.12, P1-T12 "Done
/// when").
///
/// TSPL is line-oriented ASCII, unlike ESC/POS, so the raw bytes decoded as text already are the
/// readable command listing - there is no need for a separate dump tool the way
/// <c>EscPosDump</c> exists for the binary receipt protocol. <c>HW-T03</c> prints exactly this
/// stream on the shop's own label printer and scans the barcode back.
/// </summary>
public sealed class TsplLabelRendererTests
{
    [Fact]
    public Task FR_2_10_TheTsplByteStreamForAKnownProductMatchesTheCommittedSnapshot()
    {
        var batch = new LabelBatch(
            [
                new LabelBatchItem(
                    "Galvanised bolt M8",
                    "BOLT-M8-50",
                    "2012345678905",
                    "pc",
                    Money.FromDecimal(125.00m),
                    QuantityPerLabel: 3),
            ]);

        var layout = new LabelSettings(
            WidthMm: 40,
            HeightMm: 30,
            GapMm: 2,
            ShowProductName: true,
            ShowCode: true,
            ShowBarcode: true,
            ShowUnit: true,
            ShowPrice: true,
            DefaultQuantityPerLabel: 1);

        var bytes = new TsplLabelRenderer(new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(batch, layout);

        return Verifier.Verify(Encoding.ASCII.GetString(bytes)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task FR_2_10_ALabelWithAFieldSwitchedOffOmitsItsCommandsAndTheByteStreamIsDifferent()
    {
        var batch = new LabelBatch(
            [
                new LabelBatchItem(
                    "Galvanised bolt M8",
                    "BOLT-M8-50",
                    "2012345678905",
                    "pc",
                    Money.FromDecimal(125.00m),
                    QuantityPerLabel: 3),
            ]);

        // Price switched off (Q-15's "content is configurable in settings"): the byte stream
        // must lose exactly the price command and nothing else, proving the layout is settings
        // driven rather than baked into the renderer.
        var layout = new LabelSettings(
            WidthMm: 40,
            HeightMm: 30,
            GapMm: 2,
            ShowProductName: true,
            ShowCode: true,
            ShowBarcode: true,
            ShowUnit: true,
            ShowPrice: false,
            DefaultQuantityPerLabel: 1);

        var bytes = new TsplLabelRenderer(new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(batch, layout);

        return Verifier.Verify(Encoding.ASCII.GetString(bytes)).UseDirectory("Snapshots");
    }
}

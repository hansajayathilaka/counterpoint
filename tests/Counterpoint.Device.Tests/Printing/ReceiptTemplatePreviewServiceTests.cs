using Counterpoint.Application.Settings;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing.Templates;
using Counterpoint.Domain.Services;
using FluentAssertions;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The settings screen's template preview: text on screen, nothing queued and nothing printed
/// (P1-T11's "Template preview in settings that renders to screen without printing").
/// </summary>
public sealed class ReceiptTemplatePreviewServiceTests
{
    [Fact]
    public void ItRendersTheDefaultTemplateAgainstTheSpecimenBill()
    {
        var service = new ReceiptTemplatePreviewService(
            new FixedSettings(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2));

        var lines = service.Preview(ReceiptTemplateDefaults.SalesBillTemplate);

        lines.Should().Contain(line => line.Contains("INV-2026-004312"));
        lines.Should().Contain(line => line.Contains("2000.00"), "the specimen's total");
    }

    [Fact]
    public void AChangedTemplateChangesThePreview()
    {
        var service = new ReceiptTemplatePreviewService(
            new FixedSettings(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2));

        var lines = service.Preview("TEXT|C|1|1|A whole new layout");

        lines.Should().ContainSingle(line => line.Contains("A whole new layout"));
    }

    [Fact]
    public void ABrokenTemplateExplainsItselfRatherThanThrowing()
    {
        var service = new ReceiptTemplatePreviewService(
            new FixedSettings(),
            new HalfAwayFromZeroRounding(decimalPlaces: 2));

        var act = () => service.Preview("{{ this does not parse");

        act.Should().NotThrow();
        service.Preview("{{ this does not parse").Should().Contain(
            line => line.Contains("could not be rendered"));
    }
}

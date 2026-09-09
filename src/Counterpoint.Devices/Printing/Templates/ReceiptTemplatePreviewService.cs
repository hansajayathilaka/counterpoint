using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// Renders a candidate receipt template to plain text against the §10.1 specimen bill, for the
/// settings screen's "preview" box (P1-T11's "Template preview in settings that renders to
/// screen without printing").
/// </summary>
/// <remarks>
/// Never resolves <see cref="IReceiptPrinter"/> or <see cref="Application.Abstractions.Persistence.IPrintJobOutbox"/>
/// - there is nothing here for either to do. The specimen is a fixed, made-up bill (four lines, a
/// discount, a cash tender with change) so the owner sees something worth reading whether or not
/// a real sale has ever been rung up on this till.
/// </remarks>
public sealed class ReceiptTemplatePreviewService : IReceiptTemplatePreviewService
{
    private const long Pieces = 1;

    private readonly ISettings _settings;
    private readonly IRoundingPolicy _rounding;

    public ReceiptTemplatePreviewService(ISettings settings, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(rounding);

        _settings = settings;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public string[] Preview(string templateText)
    {
        var effective = string.IsNullOrWhiteSpace(templateText)
            ? ReceiptTemplateDefaults.SalesBillTemplate
            : templateText;

        try
        {
            var model = ReceiptTemplateModelBuilder.Build(
                SpecimenBill(),
                _settings.Current,
                _rounding,
                isDuplicate: false);

            var renderedText = ScribanReceiptTemplateEngine.Render(effective, model);
            var nodes = ReceiptDirectiveParser.Parse(renderedText);

            return [.. nodes.Select(ToPreviewLine)];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Not just a Scriban syntax error: a template that parses fine can still emit a
            // directive the parser or the ESC/POS writer rejects (an oversized BARCODE| payload,
            // say). Either way this is a settings-screen preview, not a sale - degrade to an
            // in-box message rather than throw out of the call.
            return [
                "The template could not be rendered:",
                ex.Message,
            ];
        }
    }

    /// <summary>
    /// One node, one line of preview text - close enough to a printed line to judge the layout
    /// by, without needing a printer capability profile at all. Style (bold, double height) does
    /// not survive to plain text; there is no font to show it in.
    /// </summary>
    private static string ToPreviewLine(ReceiptNode node) => node switch
    {
        ReceiptNode.TextLine text => Pad(text.Text, text.Align),
        ReceiptNode.Columns columns => columns.Left.PadRight(30) + columns.Right.PadLeft(18),
        ReceiptNode.Divider => new string('-', 48),
        ReceiptNode.Barcode barcode => "[barcode: " + barcode.Data + "]",
        ReceiptNode.RasterBarcode raster => "[barcode: " + raster.Data + "]",
        ReceiptNode.QrCode qr => "[QR: " + qr.Data + "]",
        ReceiptNode.Feed => string.Empty,
        ReceiptNode.Cut => "--- cut ---",
        ReceiptNode.Kick => string.Empty,
        _ => string.Empty,
    };

    private static string Pad(string text, TextAlign align) => align switch
    {
        TextAlign.Centre => Centre(text, 48),
        TextAlign.Right => text.PadLeft(48),
        _ => text,
    };

    private static string Centre(string text, int width) =>
        text.Length >= width ? text : new string(' ', (width - text.Length) / 2) + text;

    /// <summary>The SRS §10.1 specimen, as a <see cref="SaleReceipt"/> the template model builds from.</summary>
    private static SaleReceipt SpecimenBill()
    {
        var lines = new List<SaleReceiptLine>
        {
            new("Hex Bolt M10x50 Zn", Quantity.FromDecimal(20m, Pieces), "pcs", Money.FromDecimal(25.00m), Money.FromDecimal(500.00m)),
            new("PVC Elbow 1\" 90deg", Quantity.FromDecimal(4m, Pieces), "pcs", Money.FromDecimal(90.00m), Money.FromDecimal(360.00m)),
            new("Cable 2.5mm 3-core", Quantity.FromDecimal(2.75m, Pieces), "m", Money.FromDecimal(420.00m), Money.FromDecimal(1155.00m)),
            new("Cutting charge", Quantity.FromDecimal(1m, Pieces), "svc", Money.FromDecimal(50.00m), Money.FromDecimal(50.00m)),
        };

        return new SaleReceipt(
            "INV-2026-004312",
            new DateTimeOffset(2026, 9, 3, 14, 7, 0, TimeSpan.FromHours(5.5)),
            lines,
            Money.FromDecimal(2065.00m),
            Money.FromDecimal(65.00m),
            Money.FromDecimal(2000.00m),
            Money.Zero,
            Money.FromDecimal(2000.00m),
            [new SaleReceiptTender("CASH", Money.FromDecimal(2500.00m))],
            Money.FromDecimal(500.00m),
            [new SaleReceiptTaxLine("Tax @ 0%", Money.FromDecimal(2000.00m), Money.Zero)],
            "Kamal",
            "Walk-in",
            IsTradeCustomer: false);
    }
}

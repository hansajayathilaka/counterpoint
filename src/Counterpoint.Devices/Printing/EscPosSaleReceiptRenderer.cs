using System;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Devices.Printing.Templates;
using Counterpoint.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a completed bill into ESC/POS bytes, through the owner-editable Scriban template
/// (SRS FR-7.1, FR-7.3, FR-7.4, FR-7.5, FR-7.6, NFR-M1), the receipt IR and
/// <see cref="EscPosRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pipeline:</b> <see cref="Render"/> builds a <see cref="ReceiptTemplateModel"/> from the
/// bill and the shop's settings (<see cref="ReceiptTemplateModelBuilder"/>), renders
/// <c>settings.Receipt.TemplateText</c> against it through
/// <see cref="ScribanReceiptTemplateEngine"/>, turns the rendered text into
/// <see cref="ReceiptNode"/>s through <see cref="ReceiptDirectiveParser"/>, and hands those to
/// <see cref="EscPosRenderer"/> for the bytes. P0-T05's <c>SpecimenReceipt</c> built the IR by
/// hand because the layout it needed did not exist yet; this is that layout, read from the
/// database on every call, so an owner's edit changes the next bill printed with no rebuild.
/// </para>
/// <para>
/// <b>A broken template degrades; it never blocks a sale.</b> If the stored template fails to
/// parse, throws while rendering, or parses fine but emits a directive the parser or the ESC/POS
/// writer rejects (an oversized <c>BARCODE|</c> payload, say), this logs a warning and falls back
/// to <see cref="ReceiptTemplateDefaults.SalesBillTemplate"/> - the same §10.1 specimen a fresh
/// install ships with (CLAUDE.md invariant 7's spirit: an owner's typo in Settings is not
/// grounds to stop the till printing).
/// </para>
/// <para>
/// Pure: no device, no file, no clock. It is therefore safe to call inside the sale transaction,
/// which is where the bill number becomes available (CLAUDE.md invariant 7 bans the <i>printer
/// call</i>, not the rendering). <see cref="ISettings.Current"/> is a field read off an in-memory
/// cache (<c>SettingsService</c>), not a database round trip.
/// </para>
/// </remarks>
public sealed partial class EscPosSaleReceiptRenderer : ISaleReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;
    private readonly ISettings _settings;
    private readonly ILogger<EscPosSaleReceiptRenderer> _logger;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - the
    /// amounts arriving here are already rounded (CLAUDE.md invariant 2).
    /// </param>
    /// <param name="settings">
    /// The shop's settings - the template body itself, the shop profile, and the receipt toggles
    /// the template reads (SRS FR-10.8).
    /// </param>
    /// <param name="logger">Where a broken owner-edited template is warned about.</param>
    public EscPosSaleReceiptRenderer(
        EscPosRenderer renderer,
        IRoundingPolicy rounding,
        ISettings settings,
        ILogger<EscPosSaleReceiptRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _renderer = renderer;
        _rounding = rounding;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public byte[] Render(SaleReceipt receipt, bool isDuplicate = false)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var settings = _settings.Current;
        var model = ReceiptTemplateModelBuilder.Build(receipt, settings, _rounding, isDuplicate);

        var effective = string.IsNullOrWhiteSpace(settings.Receipt.TemplateText)
            ? ReceiptTemplateDefaults.SalesBillTemplate
            : settings.Receipt.TemplateText;

        try
        {
            return RunPipeline(effective, model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (ReferenceEquals(effective, ReceiptTemplateDefaults.SalesBillTemplate))
            {
                // The shipped default itself does not render - a bug in this codebase, not an
                // owner's edit. Nothing safe is left to fall back to.
                throw;
            }

            TemplateFellBackToDefault(ex);
            return RunPipeline(ReceiptTemplateDefaults.SalesBillTemplate, model);
        }
    }

    /// <summary>
    /// The whole per-attempt pipeline: Scriban render, directive parse, IR render to bytes.
    /// </summary>
    /// <remarks>
    /// A single unit so that <see cref="Render"/> can fall back to the shipped default for any
    /// failure anywhere in it - not just a Scriban syntax error, but an owner-edited template
    /// that parses fine yet emits a directive <see cref="ReceiptDirectiveParser"/> or
    /// <see cref="EscPosRenderer"/> rejects (for example a <c>BARCODE|</c> line whose interpolated
    /// data is empty or over 255 bytes). None of that is grounds to stop the till printing
    /// (CLAUDE.md invariant 7).
    /// </remarks>
    private byte[] RunPipeline(string templateText, ReceiptTemplateModel model)
    {
        var renderedText = ScribanReceiptTemplateEngine.Render(templateText, model);
        var nodes = ReceiptDirectiveParser.Parse(renderedText);

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Warning,
        Message = "The owner-edited receipt template could not be rendered; this bill printed "
            + "with the shipped default template instead. Fix the template in Settings.")]
    private partial void TemplateFellBackToDefault(Exception exception);
}

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Renders a receipt template to plain text for the settings screen - a preview, never a print
/// job (P1-T11's "template preview in settings that renders to screen without printing").
/// </summary>
/// <remarks>
/// Never touches <see cref="IReceiptPrinter"/> or the print outbox: this is the one caller of the
/// template engine that is guaranteed not to produce a <c>print_job</c> row, so an owner can try
/// a template edit as many times as it takes without queuing a single document.
/// </remarks>
public interface IReceiptTemplatePreviewService
{
    /// <summary>
    /// Renders <paramref name="templateText"/> against a fixed specimen bill (the §10.1 sample
    /// data), one printed line per array entry, so the settings screen can show it in a
    /// monospaced box the width of the shop's paper.
    /// </summary>
    /// <param name="templateText">The template text to try - not necessarily saved yet.</param>
    /// <returns>
    /// The rendered lines, or a single line explaining the problem if the template does not
    /// parse or render (never an exception: a broken edit is exactly what a preview exists to
    /// catch before it is saved).
    /// </returns>
    public string[] Preview(string templateText);
}

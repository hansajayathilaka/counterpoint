using System;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// A receipt template could not be parsed or rendered (SRS FR-7.3). Never thrown across a sale:
/// the renderer that calls <see cref="ScribanReceiptTemplateEngine"/> catches this and falls back
/// to the shipped default template rather than let a broken owner edit stop a sale from printing
/// (CLAUDE.md invariant 7).
/// </summary>
public sealed class ReceiptTemplateException : Exception
{
    public ReceiptTemplateException(string message)
        : base(message)
    {
    }

    public ReceiptTemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

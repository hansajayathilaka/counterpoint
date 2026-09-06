namespace Counterpoint.Devices.Printing;

/// <summary>
/// Horizontal alignment of a receipt line. The values are the <c>n</c> of <c>ESC a n</c>,
/// so a renderer never has to translate them.
/// </summary>
public enum TextAlign
{
    /// <summary>Flush left. The default for everything except headings and barcodes.</summary>
    Left = 0,

    /// <summary>Centred - shop name, address, barcode, thank-you line.</summary>
    Centre = 1,

    /// <summary>Flush right.</summary>
    Right = 2,
}

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The 1D barcode symbologies the receipts use (SRS PRT-04, FR-7.4).
///
/// The values are the <c>m</c> parameter of <c>GS k</c> function B, the form that carries an
/// explicit data length instead of a NUL terminator. A printer that only implements function A
/// is a quirk for <c>HW-T01</c> to record, not a reason to change these names.
/// </summary>
public enum BarcodeSymbology
{
    /// <summary>UPC-A. <c>GS k 65</c>.</summary>
    UpcA = 65,

    /// <summary>EAN-13, the retail article number on most packaged goods. <c>GS k 67</c>.</summary>
    Ean13 = 67,

    /// <summary>EAN-8. <c>GS k 68</c>.</summary>
    Ean8 = 68,

    /// <summary>Code 39. <c>GS k 69</c>.</summary>
    Code39 = 69,

    /// <summary>Interleaved 2 of 5. <c>GS k 70</c>.</summary>
    Interleaved2Of5 = 70,

    /// <summary>
    /// Code 128 - the default for a bill number, because it encodes the full ASCII bill
    /// number in the least space (SRS PRT-04). <c>GS k 73</c>.
    /// </summary>
    Code128 = 73,
}

using System.Text;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// What this particular printer can be trusted to do, and how.
///
/// Printers claim ESC/POS and then differ - on the cutter, the code page, the native barcode,
/// the drawer pin. Every command the renderer emits is gated on a flag here, so a quirk found
/// on the shop's unit in <c>HW-T01</c> is fixed by changing a setting, not by editing
/// <see cref="EscPosRenderer"/>. Findings go in <c>docs/adr/printer-quirks.md</c>.
///
/// Turning a capability off degrades: the command is simply not emitted. A printer with no
/// cutter still prints the bill.
/// </summary>
public sealed record PrinterCapabilities
{
    /// <summary>Paper geometry (SRS PRT-01). 80 mm by default.</summary>
    public ReceiptLayout Layout { get; init; } = ReceiptLayout.EightyMillimetre;

    /// <summary>Emit <c>ESC @</c> at the start of a document.</summary>
    public bool SupportsInitialise { get; init; } = true;

    /// <summary>Emit <c>ESC t n</c> to select a code page.</summary>
    public bool SupportsCodePage { get; init; } = true;

    /// <summary>The code page to select. Ignored unless <see cref="SupportsCodePage"/>.</summary>
    public byte CodePage { get; init; } = EscPos.CodePage437;

    /// <summary>
    /// How characters become bytes. ASCII by default, which replaces anything outside it with
    /// <c>?</c> rather than printing rubbish. A shop that needs accented Latin switches this
    /// and <see cref="CodePage"/> together.
    /// </summary>
    public Encoding TextEncoding { get; init; } = Encoding.ASCII;

    /// <summary>Emit <c>ESC a n</c>. When false, the renderer centres and right-aligns with spaces.</summary>
    public bool SupportsAlign { get; init; } = true;

    /// <summary>Emit <c>ESC E n</c> for emphasis.</summary>
    public bool SupportsBold { get; init; } = true;

    /// <summary>
    /// Emit <c>GS ! n</c> for double height and width (SRS PRT-03). When false, the total
    /// still prints - at normal size, across the full line width.
    /// </summary>
    public bool SupportsDoubleSize { get; init; } = true;

    /// <summary>Fire the auto-cutter (SRS PRT-05).</summary>
    public bool SupportsCut { get; init; } = true;

    /// <summary>Which cut command to use.</summary>
    public CutMode CutMode { get; init; } = CutMode.Partial;

    /// <summary>
    /// Lines fed before the cut, so the printed text clears the cutter and the tear-off is
    /// readable.
    /// </summary>
    public int FeedLinesBeforeCut { get; init; } = 4;

    /// <summary>Pulse the drawer kick-out port (SRS FR-7.7).</summary>
    public bool SupportsDrawerKick { get; init; } = true;

    /// <summary>Which kick-out pin the drawer is wired to.</summary>
    public DrawerPin DrawerPin { get; init; } = DrawerPin.Pin2;

    /// <summary>
    /// How long the kick-out pulse is held on, in units of 2 ms. The conventional 25 (50 ms)
    /// by default; a drawer with a heavy solenoid needs 50 to 100 and simply does not open at
    /// 25 - the single most common cash-drawer quirk, and one <c>HW-T01</c> fixes here rather
    /// than in <see cref="EscPosRenderer"/>.
    /// </summary>
    public byte DrawerPulseOnTime { get; init; } = 25;

    /// <summary>
    /// How long the pulse stays off afterwards, in units of 2 ms, before the printer will
    /// honour another kick. 250 (500 ms) by default.
    /// </summary>
    public byte DrawerPulseOffTime { get; init; } = 250;

    /// <summary>Native <c>GS k</c> or a <c>GS v 0</c> raster (SRS PRT-04).</summary>
    public BarcodeMode BarcodeMode { get; init; } = BarcodeMode.Native;

    /// <summary>Bar height in dots.</summary>
    public byte BarcodeHeightDots { get; init; } = 80;

    /// <summary>Module width in dots, 2 to 6.</summary>
    public byte BarcodeModuleWidth { get; init; } = 2;

    /// <summary>Where the printer puts the digits under the bars.</summary>
    public HriPosition BarcodeHriPosition { get; init; } = HriPosition.Below;

    /// <summary>Font for those digits. 0 is font A.</summary>
    public byte BarcodeHriFont { get; init; }

    /// <summary>
    /// Prefix Code 128 data with the <c>{B</c> code-set selector. Printers disagree on
    /// whether they insert it themselves; the ones that do not print nothing without it.
    /// </summary>
    public bool PrefixCode128CodeSet { get; init; } = true;

    /// <summary>Print QR codes with <c>GS ( k</c>.</summary>
    public bool SupportsQrCode { get; init; } = true;

    /// <summary>QR module size in dots.</summary>
    public byte QrCodeModuleSize { get; init; } = 6;

    /// <summary>QR error correction: 48 = L, 49 = M, 50 = Q, 51 = H.</summary>
    public byte QrCodeErrorCorrection { get; init; } = 49;

    /// <summary>Print bitmaps with <c>GS v 0</c>.</summary>
    public bool SupportsRaster { get; init; } = true;

    /// <summary>A standard 80 mm ESC/POS printer with a cutter and a drawer port.</summary>
    public static PrinterCapabilities Default { get; } = new();

    /// <summary>The same printer on 58 mm paper (SRS PRT-01).</summary>
    public static PrinterCapabilities FiftyEightMillimetre { get; } =
        Default with { Layout = ReceiptLayout.FiftyEightMillimetre };
}

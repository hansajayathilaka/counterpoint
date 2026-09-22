using System.Collections.Generic;
using System.Linq;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T14's own contrast proof (SRS UI-01, UI-03, UI-09, NFR-U4), following the pattern of
/// task P3-T10's <see cref="ThemeTokenContrastTests"/>: arithmetic on the actual resolved token
/// colours, not a screenshot, this time for exactly the two surfaces the task's own context names
/// as the confirmed defect - the sales screen's side panel and its running-total banner.
/// </summary>
/// <remarks>
/// <para>
/// <c>SalesWindow.axaml</c>'s total banner and <c>SalesSidePanelView.axaml</c>'s side panel are
/// both built from <c>PanelBackgroundBrush</c> as their background, with
/// <c>PrimaryTextBrush</c>/<c>SecondaryTextBrush</c>/<c>WarningBrush</c>/<c>TotalHighlightBrush</c>
/// as the foregrounds drawn on it (see those two files' own remarks) - every one of these four
/// pairs is already a "text pair" <see cref="ThemeTokenContrastTests"/> holds to WCAG AA's 4.5:1
/// minimum at the token level. This class re-asserts the same four pairs under names that read as
/// "the sales screen" rather than "the token set", so a future change to what colour
/// <c>SalesWindow</c>/<c>SalesSidePanelView</c> actually uses is what breaks this test, not an
/// unrelated token-level assertion happening to cover it by coincidence.
/// </para>
/// </remarks>
public sealed class SalesScreenContrastTests
{
    private const double MinimumTextContrast = 4.5;

    /// <summary>
    /// Every foreground the side panel or the total banner actually draws against
    /// <c>PanelBackgroundBrush</c> (see <c>SalesWindow.axaml</c>'s total banner and
    /// <c>SalesSidePanelView.axaml</c>'s <c>Border</c>).
    /// </summary>
    private static readonly string[] PanelForegrounds =
    [
        "PrimaryTextBrush",
        "SecondaryTextBrush",
        "WarningBrush",
        "TotalHighlightBrush",
    ];

    public static IEnumerable<object[]> PanelForegroundsData() =>
        PanelForegrounds.Select(foreground => new object[] { foreground });

    [Theory]
    [MemberData(nameof(PanelForegroundsData))]
    public void UI_03_TheSidePanelAndTotalBannerMeetWcagAaInLight(string foreground)
    {
        AssertContrast(ThemeTokenContrastTests.LoadPalette("Tokens.Light.axaml"), foreground, "Light");
    }

    [Theory]
    [MemberData(nameof(PanelForegroundsData))]
    public void UI_03_TheSidePanelAndTotalBannerMeetWcagAaInDark(string foreground)
    {
        AssertContrast(ThemeTokenContrastTests.LoadPalette("Tokens.Dark.axaml"), foreground, "Dark");
    }

    private static void AssertContrast(Dictionary<string, string> palette, string foregroundKey, string variant)
    {
        const string backgroundKey = "PanelBackgroundBrush";

        palette.Should().ContainKey(foregroundKey);
        palette.Should().ContainKey(backgroundKey);

        var ratio = ThemeTokenContrastTests.ContrastRatio(palette[foregroundKey], palette[backgroundKey]);

        ratio.Should().BeGreaterThanOrEqualTo(
            MinimumTextContrast,
            "{0} theme's sales-screen side panel/total banner foreground {1} on {2} must meet at "
                + "least {3}:1, and resolved to {4:0.00}:1",
            variant,
            foregroundKey,
            backgroundKey,
            MinimumTextContrast,
            ratio);
    }

    /// <summary>
    /// Task P3-T21's own restyle onto the P3-T17 extended token set (SRS UI-01, UI-02, UI-03,
    /// NFR-U4) - a purely cosmetic reskin of <c>SalesWindow.axaml</c>/
    /// <c>SalesSidePanelView.axaml</c>, proven the same arithmetic-on-resolved-colours way as
    /// every pair above. Every pair here is a foreground/background combination one of those two
    /// files actually draws, introduced by this task:
    /// <list type="bullet">
    /// <item>the running-total banner's own background moved from <c>PanelBackgroundBrush</c> to
    /// <c>SuccessTintBrush</c>, so every foreground the banner draws directly against it
    /// (<c>TotalHighlightBrush</c> for the total figure itself, <c>PrimaryTextBrush</c> for the
    /// Subtotal/Tax amounts, <c>SecondaryTextBrush</c> for their captions and the current-customer
    /// line) needs its own proof against that new background;</item>
    /// <item>the small "TOTAL"/section-heading chips use <c>BrandTintBrush</c> as their
    /// background, with <c>PrimaryTextBrush</c>/<c>SecondaryTextBrush</c> as their foreground;</item>
    /// <item>the discount figure's own chip, the bill line's remove ("x") button and the F7
    /// panel's Clear button all use <c>WarningBrush</c> text on <c>DangerTintBrush</c>.</item>
    /// </list>
    /// <c>TotalHighlightBrush</c>/<c>SuccessBrush</c> and <c>WarningBrush</c>/<c>DangerBrush</c>
    /// are each defined to the exact same colour in both token files (see
    /// <c>Tokens.Light.axaml</c>/<c>Tokens.Dark.axaml</c>'s own remarks) - this class still proves
    /// each pair under the name the screen actually uses, rather than relying on that coincidence
    /// holding forever. F9 Pay and the Pay panel's "Complete sale" button reuse
    /// <c>RailActiveTextBrush</c>/<c>RailActiveBackgroundBrush</c>, a pair
    /// <see cref="ThemeTokenContrastTests"/> already proves - not re-tested here.
    /// </summary>
    private static readonly (string Foreground, string Background)[] RestyledSurfacePairs =
    [
        ("TotalHighlightBrush", "SuccessTintBrush"),
        ("PrimaryTextBrush", "SuccessTintBrush"),
        ("SecondaryTextBrush", "SuccessTintBrush"),
        ("PrimaryTextBrush", "BrandTintBrush"),
        ("SecondaryTextBrush", "BrandTintBrush"),
        ("WarningBrush", "DangerTintBrush"),
    ];

    public static IEnumerable<object[]> RestyledSurfacePairsData() =>
        RestyledSurfacePairs.Select(pair => new object[] { pair.Foreground, pair.Background });

    [Theory]
    [MemberData(nameof(RestyledSurfacePairsData))]
    public void UI_03_TheRestyledSalesScreenSurfacesMeetWcagAaInLight(string foreground, string background)
    {
        AssertSurfaceContrast(ThemeTokenContrastTests.LoadPalette("Tokens.Light.axaml"), foreground, background, "Light");
    }

    [Theory]
    [MemberData(nameof(RestyledSurfacePairsData))]
    public void UI_03_TheRestyledSalesScreenSurfacesMeetWcagAaInDark(string foreground, string background)
    {
        AssertSurfaceContrast(ThemeTokenContrastTests.LoadPalette("Tokens.Dark.axaml"), foreground, background, "Dark");
    }

    private static void AssertSurfaceContrast(
        Dictionary<string, string> palette,
        string foregroundKey,
        string backgroundKey,
        string variant)
    {
        palette.Should().ContainKey(foregroundKey);
        palette.Should().ContainKey(backgroundKey);

        var ratio = ThemeTokenContrastTests.ContrastRatio(palette[foregroundKey], palette[backgroundKey]);

        ratio.Should().BeGreaterThanOrEqualTo(
            MinimumTextContrast,
            "{0} theme's restyled sales-screen foreground {1} on {2} must meet at least {3}:1, "
                + "and resolved to {4:0.00}:1",
            variant,
            foregroundKey,
            backgroundKey,
            MinimumTextContrast,
            ratio);
    }
}

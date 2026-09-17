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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// <b>AC-21</b> — "Every screen renders with adequate contrast in both the Light and Dark theme
/// (verified by an automated contrast-ratio test, not by opinion)." Task P3-T16's closing gate for
/// the whole UI redesign.
/// </summary>
/// <remarks>
/// <para>
/// This is a two-part deduction, not a new kind of check: <see cref="ThemeTokenContrastTests"/>
/// already proves every documented foreground/background token pair meets WCAG AA's 4.5:1 (text)
/// or 3:1 (non-text, <c>AccentBrush</c>) minimum in both variants, by arithmetic on the resolved
/// colours; <see cref="NoRawHexColourLiteralsTests"/> already proves every screen under
/// <c>src/Counterpoint.Ui/Views/</c> draws exclusively through those same semantic token keys,
/// never a colour of its own. Put the two together and "every screen is legible in both themes"
/// follows: a screen that cannot bypass the tokens (the first proof) and tokens that are all
/// individually legible (the second proof) cannot together produce an illegible screen. This test
/// exists to state that conclusion once, explicitly, as its own named acceptance criterion, rather
/// than leaving a reader to infer AC-21 from two differently-purposed test classes.
/// </para>
/// <para>
/// Nothing here re-implements WCAG's contrast formula or the hex-literal regex - both are reused
/// verbatim from <see cref="ThemeTokenContrastTests"/> (<see cref="ThemeTokenContrastTests.LoadPalette"/>,
/// <see cref="ThemeTokenContrastTests.ContrastRatio"/>, <see cref="ThemeTokenContrastTests.TextPairs"/>)
/// and <see cref="NoRawHexColourLiteralsTests"/> (<see cref="NoRawHexColourLiteralsTests.AllViewAxamlFiles"/>,
/// <see cref="NoRawHexColourLiteralsTests.FindHexOffences"/>).
/// </para>
/// </remarks>
public sealed class AC21_EveryScreenIsLegibleInBothThemes
{
    [Fact]
    public void AC_21_NoScreenDrawsAColourOfItsOwnThatTheTokenContrastProofDoesNotCover()
    {
        var files = NoRawHexColourLiteralsTests.AllViewAxamlFiles();
        var offenders = new List<string>();

        foreach (var file in files)
        {
            offenders.AddRange(NoRawHexColourLiteralsTests.FindHexOffences(file));
        }

        offenders.Should().BeEmpty(
            "AC-21 only follows from the token contrast proof below if every screen actually draws "
            + "through the tokens rather than a colour of its own. Offenders: "
            + string.Join("; ", offenders));
    }

    [Theory]
    [InlineData("Tokens.Light.axaml", "Light")]
    [InlineData("Tokens.Dark.axaml", "Dark")]
    public void AC_21_EveryDocumentedTextPairMeetsWcagAaInThisVariant(string fileName, string variant)
    {
        var palette = ThemeTokenContrastTests.LoadPalette(fileName);

        foreach (var (foreground, background) in ThemeTokenContrastTests.TextPairs)
        {
            var ratio = ThemeTokenContrastTests.ContrastRatio(palette[foreground], palette[background]);

            ratio.Should().BeGreaterThanOrEqualTo(
                4.5,
                "{0} theme's {1} on {2} must meet WCAG AA's 4.5:1 for every screen built from it, "
                + "and resolved to {3:0.00}:1",
                variant,
                foreground,
                background,
                ratio);
        }
    }

    [Theory]
    [InlineData("Tokens.Light.axaml", "Light")]
    [InlineData("Tokens.Dark.axaml", "Dark")]
    public void AC_21_TheAccentMeetsTheNonTextThresholdInThisVariant(string fileName, string variant)
    {
        var palette = ThemeTokenContrastTests.LoadPalette(fileName);

        var ratio = ThemeTokenContrastTests.ContrastRatio(palette["AccentBrush"], palette["WindowBackgroundBrush"]);

        ratio.Should().BeGreaterThanOrEqualTo(
            3.0,
            "{0} theme's AccentBrush on WindowBackgroundBrush must meet WCAG's 3:1 non-text "
            + "threshold, and resolved to {1:0.00}:1",
            variant,
            ratio);
    }
}

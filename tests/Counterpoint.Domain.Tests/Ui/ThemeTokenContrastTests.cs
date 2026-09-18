using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T10's contrast proof: arithmetic on the actual resolved colours in
/// <c>Styles/Tokens.Light.axaml</c> and <c>Styles/Tokens.Dark.axaml</c>, not a screenshot
/// (SRS UI-13, NFR-U4).
/// </summary>
/// <remarks>
/// WCAG 2.1 Success Criterion 1.4.3 (AA) sets 4.5:1 as the minimum contrast ratio between normal
/// text and its background - the "documented minimum ratio" the phase 3 task doc calls for, and
/// the standard baseline for body text. Every pair tested here is a foreground brush this token
/// set actually uses to draw text (<c>PrimaryTextBrush</c>, <c>SecondaryTextBrush</c>,
/// <c>WarningBrush</c>, <c>TotalHighlightBrush</c>) against a background brush a screen actually
/// places it on (<c>WindowBackgroundBrush</c>, <c>PanelBackgroundBrush</c>).
/// <c>PanelBorderBrush</c> is decorative chrome, not text, deliberately subtle against the panel
/// it outlines, and is not held to a text-contrast ratio at all -
/// <see cref="UI_13_AccentMeetsTheNonTextThreeToOneThreshold"/> proves the one non-text
/// (WCAG SC 1.4.11) minimum this token set does commit to: <c>AccentBrush</c> against the plain
/// window background it is meant to stand out on.
/// </remarks>
public sealed class ThemeTokenContrastTests
{
    private const string SolutionFileName = "Counterpoint.sln";
    private const double MinimumTextContrast = 4.5;
    private const double MinimumNonTextContrast = 3.0;

    private static readonly XNamespace AvaloniaNamespace = "https://github.com/avaloniaui";
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every foreground-on-background pair a Light/Dark token set must keep legible. Internal, not
    /// private: task P3-T16's <see cref="AC21_EveryScreenIsLegibleInBothThemes"/> reuses this
    /// exact list (and <see cref="LoadPalette"/>/<see cref="ContrastRatio"/> below) as its
    /// consolidated, repository-wide acceptance gate, rather than a second, divergently-scoped
    /// copy of the same pairs.
    /// </summary>
    internal static readonly (string Foreground, string Background)[] TextPairs =
    [
        ("PrimaryTextBrush", "WindowBackgroundBrush"),
        ("PrimaryTextBrush", "PanelBackgroundBrush"),
        ("SecondaryTextBrush", "WindowBackgroundBrush"),
        ("SecondaryTextBrush", "PanelBackgroundBrush"),
        ("WarningBrush", "WindowBackgroundBrush"),
        ("WarningBrush", "PanelBackgroundBrush"),
        ("TotalHighlightBrush", "WindowBackgroundBrush"),
        ("TotalHighlightBrush", "PanelBackgroundBrush"),
    ];

    public static IEnumerable<object[]> TextPairsData() =>
        TextPairs.Select(pair => new object[] { pair.Foreground, pair.Background });

    [Theory]
    [MemberData(nameof(TextPairsData))]
    public void UI_13_LightThemeTextPairsMeetWcagAaFourPointFiveToOne(string foreground, string background)
    {
        AssertContrast(LoadPalette("Tokens.Light.axaml"), foreground, background, MinimumTextContrast, "Light");
    }

    [Theory]
    [MemberData(nameof(TextPairsData))]
    public void UI_13_DarkThemeTextPairsMeetWcagAaFourPointFiveToOne(string foreground, string background)
    {
        AssertContrast(LoadPalette("Tokens.Dark.axaml"), foreground, background, MinimumTextContrast, "Dark");
    }

    [Theory]
    [InlineData("Tokens.Light.axaml", "Light")]
    [InlineData("Tokens.Dark.axaml", "Dark")]
    public void UI_13_AccentMeetsTheNonTextThreeToOneThreshold(string fileName, string variant)
    {
        AssertContrast(LoadPalette(fileName), "AccentBrush", "WindowBackgroundBrush", MinimumNonTextContrast, variant);
    }

    [Fact]
    public void UI_13_TheTwoTokenFilesDeclareExactlyTheSameKeys()
    {
        var light = LoadPalette("Tokens.Light.axaml").Keys.OrderBy(key => key, StringComparer.Ordinal);
        var dark = LoadPalette("Tokens.Dark.axaml").Keys.OrderBy(key => key, StringComparer.Ordinal);

        light.Should().Equal(
            dark,
            "a key present in one theme dictionary and missing from the other resolves to "
            + "nothing at all the moment the shop switches variant");
    }

    private static void AssertContrast(
        Dictionary<string, string> palette,
        string foregroundKey,
        string backgroundKey,
        double minimum,
        string variant)
    {
        palette.Should().ContainKey(foregroundKey);
        palette.Should().ContainKey(backgroundKey);

        var ratio = ContrastRatio(palette[foregroundKey], palette[backgroundKey]);

        ratio.Should().BeGreaterThanOrEqualTo(
            minimum,
            "{0} theme's {1} on {2} must meet at least {3}:1, and resolved to {4:0.00}:1",
            variant,
            foregroundKey,
            backgroundKey,
            minimum,
            ratio);
    }

    /// <summary>WCAG 2.1's own contrast-ratio formula: (L1 + 0.05) / (L2 + 0.05), lighter over darker.</summary>
    internal static double ContrastRatio(string hex1, string hex2)
    {
        var l1 = RelativeLuminance(hex1);
        var l2 = RelativeLuminance(hex2);

        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>WCAG 2.1's relative luminance: linearised sRGB channels, Rec. 709 weights.</summary>
    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseRgb(hex);

        return (0.2126 * Linearise(r)) + (0.7152 * Linearise(g)) + (0.0722 * Linearise(b));
    }

    private static double Linearise(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static (int R, int G, int B) ParseRgb(string hex)
    {
        var value = hex.TrimStart('#');

        // Avalonia accepts #RGB, #RRGGBB and #AARRGGBB; only the last of the three appears in
        // this token set today, but all three are handled so a future shorthand edit does not
        // break this test silently instead of loudly.
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        if (value.Length == 8)
        {
            // Drop the leading alpha channel - contrast is computed on the colour itself.
            value = value[2..];
        }

        var r = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return (r, g, b);
    }

    /// <summary>
    /// Every <c>x:Key</c> to <c>Color</c> mapping a token file declares. Internal, not private:
    /// task P3-T14's <see cref="SalesScreenContrastTests"/> reuses this exact loader (and
    /// <see cref="ContrastRatio"/> above) rather than re-implementing the same XAML parsing, so
    /// there is exactly one place that reads a token file's colours for a test to check.
    /// </summary>
    internal static Dictionary<string, string> LoadPalette(string fileName)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Styles", fileName);

        File.Exists(path).Should().BeTrue("the token file must exist at {0}", path);

        var document = XDocument.Load(path);
        var xKey = XamlNamespace + "Key";

        var palette = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var brush in document.Descendants(AvaloniaNamespace + "SolidColorBrush"))
        {
            var key = brush.Attribute(xKey)?.Value;
            var color = brush.Attribute("Color")?.Value;

            if (key is not null && color is not null)
            {
                palette[key] = color;
            }
        }

        palette.Should().NotBeEmpty("{0} must declare at least one SolidColorBrush", fileName);

        return palette;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}

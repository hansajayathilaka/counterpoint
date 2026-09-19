using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T17's own first "Done when" line, proven at runtime (SRS UI-13, NFR-U4, NFR-M1):
/// "every new semantic and rail key resolves in both Light and Dark with no missing-resource
/// warning".
/// </summary>
/// <remarks>
/// <c>Counterpoint.Domain.Tests.Ui.ThemeTokenContrastTests</c> already proves every new pair's
/// WCAG contrast from the colours declared in <c>Tokens.Light.axaml</c>/<c>Tokens.Dark.axaml</c>'s
/// raw XML - but that is a static file parse (<c>XDocument.Load</c>), because
/// <c>Counterpoint.Domain.Tests</c> may not reference Avalonia at all (<c>CLAUDE.md</c>'s project
/// boundaries: <c>Domain -&gt; nothing</c>). A key could be spelled correctly in the file that test
/// reads and still fail to resolve at runtime - wrong <c>ResourceDictionary</c>, a duplicate key
/// shadowing it, a merge that silently excludes it - and that static parse would never catch it,
/// because it never asks Avalonia's own resource system anything.
///
/// This test closes that gap: it boots the real <c>Counterpoint.Ui.App</c> (the same
/// <see cref="TestAppBuilder"/> every other test in this project runs under - real
/// <c>App.axaml</c>, real merged <c>ResourceDictionary.ThemeDictionaries</c>, never a test-only
/// stub) and calls <c>Application.Current.TryGetResource</c> for every one of the fifteen brush
/// keys P3-T17 adds, in both variants - the exact same technique
/// <see cref="FontResourceTests.NFR_U5_TheFontResourceResolvesToTheEmbeddedFamilyInBothVariants"/>
/// already established for <c>DisplayFontFamily</c>/<c>BodyFontFamily</c> (those two keys are
/// already covered there and are not repeated here). Each resolved colour is also cross-checked
/// against the value the token file itself declares, so a resolution that silently picks up a
/// stale or shadowed brush is caught too, not just an outright missing one.
/// </remarks>
public sealed class DesignTokenResourceResolutionTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    /// <summary>
    /// The ten extended semantic keys and five rail keys P3-T17 adds - see
    /// <c>Tokens.Light.axaml</c>'s own P3-T17 section for what each one is for. Deliberately
    /// excludes the eight pre-existing P3-T10 keys (already exercised by every screen's own
    /// tests) and the two P3-T17 font keys, which <see cref="FontResourceTests"/> already proves
    /// resolve.
    /// </summary>
    private static readonly string[] NewBrushKeys =
    [
        "SurfaceBackgroundBrush",
        "SunkenSurfaceBackgroundBrush",
        "TertiaryTextBrush",
        "BrandBrush",
        "BrandHoverBrush",
        "BrandTintBrush",
        "SuccessBrush",
        "SuccessTintBrush",
        "DangerBrush",
        "DangerTintBrush",
        "RailBackgroundBrush",
        "RailActiveBackgroundBrush",
        "RailTextBrush",
        "RailActiveTextBrush",
        "RailMutedTextBrush",
    ];

    public static IEnumerable<object[]> NewBrushKeysData() =>
        NewBrushKeys.Select(key => new object[] { key });

    [AvaloniaTheory]
    [MemberData(nameof(NewBrushKeysData))]
    public void UI_13_NewTokenKeyResolvesInLightWithNoMissingResourceWarning(string key)
    {
        AssertResolvesToDeclaredColour(key, ThemeVariant.Light, "Tokens.Light.axaml");
    }

    [AvaloniaTheory]
    [MemberData(nameof(NewBrushKeysData))]
    public void UI_13_NewTokenKeyResolvesInDarkWithNoMissingResourceWarning(string key)
    {
        AssertResolvesToDeclaredColour(key, ThemeVariant.Dark, "Tokens.Dark.axaml");
    }

    private static void AssertResolvesToDeclaredColour(string key, ThemeVariant variant, string fileName)
    {
        var application = global::Avalonia.Application.Current;
        application.Should().NotBeNull();

        application!.TryGetResource(key, variant, out var value).Should().BeTrue(
            "{0} must resolve in the {1} variant with no missing-resource warning (SRS UI-13, task P3-T17)",
            key, variant);

        value.Should().BeAssignableTo<ISolidColorBrush>(
            "{0} is declared as a SolidColorBrush in {1}", key, fileName);

        var resolvedColour = ((ISolidColorBrush)value!).Color;
        var declaredColour = Color.Parse(DeclaredHexFor(key, fileName));

        resolvedColour.Should().Be(
            declaredColour,
            "{0} resolved through Application.Current in the {1} variant must be the exact colour "
            + "{2} declares for it, not a stale or shadowed value",
            key, variant, fileName);
    }

    private static string DeclaredHexFor(string key, string fileName)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Styles", fileName);
        File.Exists(path).Should().BeTrue("the token file must exist at {0}", path);

        XNamespace avaloniaNamespace = "https://github.com/avaloniaui";
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var xKey = xamlNamespace + "Key";

        var document = XDocument.Load(path);

        var brush = document.Descendants(avaloniaNamespace + "SolidColorBrush")
            .FirstOrDefault(element => element.Attribute(xKey)?.Value == key);

        brush.Should().NotBeNull("{0} must be declared as a SolidColorBrush in {1}", key, fileName);

        var colour = brush!.Attribute("Color")?.Value;
        colour.Should().NotBeNull("{0}'s SolidColorBrush in {1} must declare a Color", key, fileName);

        return colour!;
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

using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T11's file-level proofs (SRS UI-05, UI-06, UI-15, AC-23): the Category screen's old
/// always-visible inline form is gone, the new shared dialog shell and its proof-of-concept are
/// built from the P3-T10 tokens, and neither introduces a raw hex colour literal - the same
/// style <c>NoRawHexColourLiteralsTests</c> and <c>ThemeTokenContrastTests</c> already use to
/// prove a screen without opening a window (a window cannot be opened in CI).
/// </summary>
public sealed class EditDialogFrameworkTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    private static readonly Regex HexColourLiteral = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void UI_15_CategoryScreenNoLongerHasTheOldAlwaysVisibleInlineForm()
    {
        var markup = ReadUiFile("Views", "Catalogue", "CategoryTabView.axaml");

        // The confirmed defect: one inline TextBox bound straight to the "Name" field and a bare
        // "_Save" button, shared by "_New" and "_Save" with nothing naming the action or record.
        markup.Should().NotContain(
            "Watermark=\"Category name\"",
            "the inline name box the old always-visible form used must be gone");
        markup.Should().NotContain(
            "<TextBox",
            "the tab itself must hold no editable field at all - every field now lives inside "
            + "CategoryEditView, hosted by the dialog");
        markup.Should().NotContain(
            "Command=\"{Binding SaveCommand}\"",
            "there is no bare Save command left on the tab itself; saving happens inside the dialog");
    }

    [Fact]
    public void UI_15_CategoryScreenGoesThroughNewEditAndDeleteCommandsInstead()
    {
        var markup = ReadUiFile("Views", "Catalogue", "CategoryTabView.axaml");

        markup.Should().Contain("Command=\"{Binding NewCommand}\"");
        markup.Should().Contain("Command=\"{Binding EditCommand}\"");
        markup.Should().Contain("Command=\"{Binding DeleteCommand}\"");
    }

    [Fact]
    public void UI_13_EditDialogWindowHasNoRawHexColourLiteral() =>
        AssertNoHexColourLiteral("EditDialogWindow.axaml", "Views", "EditDialogWindow.axaml");

    [Fact]
    public void UI_13_CategoryEditViewHasNoRawHexColourLiteral() =>
        AssertNoHexColourLiteral("CategoryEditView.axaml", "Views", "Catalogue", "CategoryEditView.axaml");

    private static void AssertNoHexColourLiteral(string fileLabel, params string[] relativeSegments)
    {
        var markup = ReadUiFile(relativeSegments);

        var offenders = new System.Collections.Generic.List<string>();
        var lines = markup.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in HexColourLiteral.Matches(lines[i]))
            {
                offenders.Add($"{fileLabel}:{i + 1} \"{match.Value}\"");
            }
        }

        offenders.Should().BeEmpty(
            "task P3-T11's dialog files must reference a semantic DynamicResource key from "
            + "Styles/Tokens.Light.axaml/Tokens.Dark.axaml, never a hex literal (SRS UI-13, "
            + "NFR-U4). Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void UI_15_EditDialogWindowHeaderIsBoundToHeaderTextNotToAFieldsBlankness()
    {
        var markup = ReadUiFile("Views", "EditDialogWindow.axaml");

        // The header text and the window Title both come from one bound property fixed at
        // construction from the explicit DialogMode/entity/subject - never from re-reading a
        // field on the hosted content.
        markup.Should().Contain("Text=\"{Binding HeaderText}\"");
        markup.Should().Contain("Title=\"{Binding HeaderText}\"");
    }

    [Fact]
    public void UI_01_EditDialogWindowSavesOnEnterAndCancelsOnEscapeThroughIsDefaultAndIsCancel()
    {
        var markup = ReadUiFile("Views", "EditDialogWindow.axaml");

        markup.Should().Contain("IsDefault=\"True\"", "Enter must trigger the primary action (SRS UI-01)");
        markup.Should().Contain("IsCancel=\"True\"", "Escape must cancel the dialog (SRS UI-01)");
    }

    private static string ReadUiFile(params string[] relativeSegments)
    {
        var path = Path.Combine(
            RepositoryRoot().FullName,
            "src",
            "Counterpoint.Ui",
            Path.Combine(relativeSegments));

        File.Exists(path).Should().BeTrue("expected {0} to exist", path);

        return File.ReadAllText(path);
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

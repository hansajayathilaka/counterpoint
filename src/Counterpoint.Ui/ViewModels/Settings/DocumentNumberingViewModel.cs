using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.4 - the prefix, pattern and starting number of one document series.
/// </summary>
/// <remarks>
/// The starting number applies to a series that has not issued anything yet. Once a number has
/// been issued the counter is <c>number_sequence.next_val</c>'s business and nothing here moves
/// it: a number, once issued, is never reissued, and a cancelled document keeps it (CLAUDE.md
/// invariant 4, AC-19).
/// </remarks>
public sealed partial class DocumentNumberingViewModel : NumericInputViewModel
{
    private string _startingNumber = string.Empty;

    [ObservableProperty]
    private string _prefix = string.Empty;

    [ObservableProperty]
    private string _pattern = string.Empty;

    /// <summary>
    /// Whether the "Starts at" box does anything if the shop types into it. True by default, for
    /// the first-run wizard, where <see cref="StartingNumber"/> flows to
    /// <c>INumberSequenceConfiguration.InitialiseAsync</c> and really does set the series's first
    /// number. The general settings screen (<c>NumberingSettingsViewModel</c>) sets this false: a
    /// series it loads has already issued its first number or is at least past first run, and its
    /// save path is <c>ConfigureAsync</c>, which silently refuses to move an existing counter
    /// (CLAUDE.md invariant 4). Leaving the box live there invites a keystroke nothing acts on.
    /// </summary>
    [ObservableProperty]
    private bool _isStartingNumberEditable = true;

    public DocumentNumberingViewModel(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>What the shop calls this series - "Bills", "Returns".</summary>
    public string Title { get; }

    /// <summary>The first number the series issues.</summary>
    public string StartingNumber
    {
        get => _startingNumber;
        set => SetNumeric(ref _startingNumber, value);
    }

    /// <summary>Fills the boxes from the series in force.</summary>
    public void Load(DocumentNumbering series)
    {
        ArgumentNullException.ThrowIfNull(series);

        Prefix = series.Prefix;
        Pattern = series.Pattern;
        StartingNumber = SettingsText.FromLong(series.StartingNumber);
    }

    /// <summary>The series as the boxes now stand.</summary>
    public DocumentNumbering Apply(DocumentNumbering series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return new DocumentNumbering(
            Prefix.Trim(),
            Pattern.Trim(),

            // An empty box keeps the series it already has. Zero would be a number the shop
            // cannot issue, and SettingsValidation would refuse the whole save because of it.
            SettingsText.ToLong(StartingNumber, series.StartingNumber));
    }
}

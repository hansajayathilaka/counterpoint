using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The date-range picker every report screen shares (SRS FR-9.1): the six quick presets plus a
/// custom range, resolved to a <see cref="ReportDateRange"/> by the one Application-layer
/// implementation (<see cref="ReportDateRange.For"/>) - this class never does date arithmetic of its
/// own, so "last month" cannot mean two different things on two screens.
/// </summary>
public sealed partial class ReportRangeViewModel : ViewModelBase
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly EnumChoices<ReportDatePreset> _choices = new(
        (ReportDatePreset.Today, "Today"),
        (ReportDatePreset.Yesterday, "Yesterday"),
        (ReportDatePreset.ThisWeek, "This week"),
        (ReportDatePreset.ThisMonth, "This month"),
        (ReportDatePreset.LastMonth, "Last month"),
        (ReportDatePreset.ThisYear, "This year"),
        (ReportDatePreset.Custom, "Custom range"));

    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    private string _selectedPresetLabel;

    [ObservableProperty]
    private string _fromText;

    [ObservableProperty]
    private string _toText;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public ReportRangeViewModel(TimeProvider timeProvider, ReportDatePreset initial = ReportDatePreset.ThisMonth)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;

        _selectedPresetLabel = _choices.Label(initial);

        var today = Today();
        _fromText = today.ToString(DateFormat, CultureInfo.InvariantCulture);
        _toText = _fromText;
    }

    /// <summary>The labels the preset combo lists.</summary>
    public IReadOnlyList<string> PresetLabels => _choices.Labels;

    /// <summary>The preset currently chosen.</summary>
    public ReportDatePreset SelectedPreset => _choices.Value(SelectedPresetLabel);

    /// <summary>True when the owner has to type the two dates.</summary>
    public bool IsCustom => SelectedPreset == ReportDatePreset.Custom;

    /// <summary>
    /// Resolves the picker to a range, or returns false with <see cref="ValidationMessage"/> naming
    /// what is wrong (a custom date that does not read as <c>yyyy-mm-dd</c>, or an end before the start).
    /// </summary>
    public bool TryResolve(out ReportDateRange range)
    {
        ValidationMessage = string.Empty;

        if (!IsCustom)
        {
            range = ReportDateRange.For(SelectedPreset, Today());
            return true;
        }

        if (!DateOnly.TryParseExact(FromText?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(ToText?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            ValidationMessage = "Type both dates as year-month-day, for example 2026-09-30.";
            range = ReportDateRange.For(ReportDatePreset.Today, Today());
            return false;
        }

        if (to < from)
        {
            ValidationMessage = "The end date cannot be before the start date.";
            range = ReportDateRange.For(ReportDatePreset.Today, Today());
            return false;
        }

        range = ReportDateRange.Custom(from, to);
        return true;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);
}

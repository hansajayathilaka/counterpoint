using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The labels a category, brand or supplier filter combo lists, and the id each label stands for (task P3-T06).
/// The first label is "(All)", which stands for no filter.
/// </summary>
/// <remarks>
/// The combo shows plain text, so it needs no item template and the viewmodel is testable without a display - the
/// same idea as <see cref="EnumChoices{TValue}"/>. The list is filled once, on the screen's first run.
/// </remarks>
internal sealed class ReportFilterChoices
{
    internal const string AllLabel = "(All)";

    private readonly Dictionary<string, long> _idByLabel = new(StringComparer.Ordinal);

    internal ReportFilterChoices()
    {
        Labels.Add(AllLabel);
    }

    /// <summary>What the combo lists.</summary>
    internal ObservableCollection<string> Labels { get; } = [];

    /// <summary>True once <see cref="Load"/> has run.</summary>
    internal bool IsLoaded { get; private set; }

    /// <summary>Fills the list from <paramref name="options"/>; a repeated name is made unique with its id.</summary>
    internal void Load(IReadOnlyList<ReportFilterOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _idByLabel.Clear();
        Labels.Clear();
        Labels.Add(AllLabel);

        foreach (var option in options)
        {
            var label = _idByLabel.ContainsKey(option.Name)
                ? string.Create(CultureInfo.InvariantCulture, $"{option.Name} (#{option.Id})")
                : option.Name;

            _idByLabel[label] = option.Id;
            Labels.Add(label);
        }

        IsLoaded = true;
    }

    /// <summary>The id <paramref name="label"/> stands for, or null for "(All)" or nothing selected.</summary>
    internal long? IdFor(string? label) =>
        label is not null && _idByLabel.TryGetValue(label, out var id) ? id : null;

    /// <summary>The labels as a plain list, for a test to pick from.</summary>
    internal IReadOnlyList<string> AsList() => [.. Labels];
}

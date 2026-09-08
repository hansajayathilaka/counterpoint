using System;
using System.Collections.Generic;
using System.Linq;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// A fixed set of choices, shown as the words the shop uses rather than the enum member's name
/// (SRS NFR-U3).
/// </summary>
/// <remarks>
/// The list a combo box shows is plain text, so the box needs no item template and the viewmodel
/// stays testable without a display: a test picks a label the same way a person picks a row. The
/// mapping back to the value is here, in one place, rather than spread over eight screens.
/// </remarks>
/// <typeparam name="TValue">The enum the labels stand for.</typeparam>
internal sealed class EnumChoices<TValue>
    where TValue : struct, Enum
{
    private readonly (TValue Value, string Label)[] _options;

    internal EnumChoices(params (TValue Value, string Label)[] options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Length == 0)
        {
            throw new ArgumentException("A choice list needs at least one choice.", nameof(options));
        }

        _options = options;
        Labels = [.. options.Select(option => option.Label)];
    }

    /// <summary>What the combo box lists.</summary>
    internal IReadOnlyList<string> Labels { get; }

    /// <summary>The label standing for <paramref name="value"/>, or the first one.</summary>
    internal string Label(TValue value)
    {
        foreach (var option in _options)
        {
            if (EqualityComparer<TValue>.Default.Equals(option.Value, value))
            {
                return option.Label;
            }
        }

        return _options[0].Label;
    }

    /// <summary>
    /// The value <paramref name="label"/> stands for, or the first one when nothing is selected.
    /// </summary>
    internal TValue Value(string? label)
    {
        foreach (var option in _options)
        {
            if (string.Equals(option.Label, label, StringComparison.Ordinal))
            {
                return option.Value;
            }
        }

        return _options[0].Value;
    }
}

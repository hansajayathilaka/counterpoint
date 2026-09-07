using System;
using System.Runtime.CompilerServices;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// A viewmodel whose numeric boxes drop what cannot be part of a number, without saying a word
/// (SRS UI-10).
/// </summary>
/// <remarks>
/// The property keeps the cleaned text, and raises the change notification even when cleaning
/// left it exactly as it was - otherwise the box on screen would go on showing the letter the
/// binding has already thrown away.
/// </remarks>
public abstract class NumericInputViewModel : ViewModelBase
{
    /// <summary>Stores <paramref name="typed"/> with everything that is not a number removed.</summary>
    protected void SetNumeric(
        ref string field,
        string? typed,
        bool allowDecimal = false,
        bool allowNegative = false,
        [CallerMemberName] string? propertyName = null)
    {
        var cleaned = SettingsText.Sanitise(typed, allowDecimal, allowNegative);

        if (!SetProperty(ref field, cleaned, propertyName)
            && !string.Equals(cleaned, typed, StringComparison.Ordinal))
        {
            // The text box is showing a character the viewmodel refused. Tell it to redraw.
            OnPropertyChanged(propertyName);
        }
    }

    /// <summary>Stores <paramref name="typed"/> keeping only digits and the colon of a clock time.</summary>
    protected void SetTime(
        ref string field,
        string? typed,
        [CallerMemberName] string? propertyName = null)
    {
        var cleaned = SettingsText.SanitiseTime(typed);

        if (!SetProperty(ref field, cleaned, propertyName)
            && !string.Equals(cleaned, typed, StringComparison.Ordinal))
        {
            OnPropertyChanged(propertyName);
        }
    }
}

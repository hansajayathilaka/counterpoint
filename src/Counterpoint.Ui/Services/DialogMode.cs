namespace Counterpoint.Ui.Services;

/// <summary>
/// Whether <see cref="IDialogService.ShowEditDialogAsync{TViewModel}"/> is creating a brand new
/// record or editing an identified existing one (SRS UI-15, AC-23).
/// </summary>
/// <remarks>
/// This is the explicit value <c>EditDialogWindow</c>'s header is driven from. The confirmed
/// defect this task (P3-T11) replaces inferred the heading from whether a bound field happened to
/// be blank - the same inline form, shared by a "_New" button and a "_Save" button, could not
/// tell an owner whether the next Save created a category or overwrote the selected one. A
/// <see cref="DialogMode"/> is passed in by the caller and never guessed from field content.
/// </remarks>
public enum DialogMode
{
    /// <summary>The dialog is creating a new record. The header reads "New {entity}".</summary>
    Create,

    /// <summary>
    /// The dialog is editing an identified existing record. The header reads
    /// "Edit {entity} — {subject}".
    /// </summary>
    Edit,
}

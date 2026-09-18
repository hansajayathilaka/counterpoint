namespace Counterpoint.Ui.Services;

/// <summary>What the operator did with a dialog shown through <see cref="IDialogService"/>.</summary>
public enum DialogOutcome
{
    /// <summary>Escape, the Cancel button, or the window was closed without saving/confirming.</summary>
    Cancelled,

    /// <summary>Save succeeded (edit dialog) or Confirm was pressed (delete confirmation).</summary>
    Confirmed,
}

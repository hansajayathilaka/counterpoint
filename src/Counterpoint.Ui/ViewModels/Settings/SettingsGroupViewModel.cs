using System.Collections.Generic;
using System.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// One of the eight groups FR-10 divides the shop's settings into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Load and Apply, and nothing in between.</b> A group reads itself out of a
/// <see cref="SettingsSnapshot"/> and writes itself back into one; it never touches
/// <see cref="ISettings"/>, never saves, and never decides whether a value is allowed. The
/// window collects all eight into a single edited snapshot and hands that to
/// <see cref="ISettings.SaveAsync"/>, which diffs it, audits every changed key and republishes
/// the cache (FR-10.9, CLAUDE.md invariant 8).
/// </para>
/// <para>
/// <see cref="Load"/> is called every time the screen opens, not once when it is built. That is
/// the whole answer to P1-T03's stated risk - "settings read at start-up and cached forever".
/// </para>
/// </remarks>
public abstract class SettingsGroupViewModel : NumericInputViewModel
{
    /// <summary>The tab's heading, in the shop's words (SRS NFR-U3).</summary>
    public abstract string Title { get; }

    /// <summary>The SRS requirement this group is, for the person maintaining it.</summary>
    public abstract string Requirement { get; }

    /// <summary>
    /// Viewmodels nested inside this group - the six number series, and nothing else so far. The
    /// window watches them for edits the same way it watches the group itself.
    /// </summary>
    public virtual IEnumerable<INotifyPropertyChanged> Children => [];

    /// <summary>Fills the boxes from the settings in force.</summary>
    public abstract void Load(SettingsSnapshot snapshot);

    /// <summary>Returns <paramref name="snapshot"/> with this group's boxes written into it.</summary>
    public abstract SettingsSnapshot Apply(SettingsSnapshot snapshot);

    /// <summary>
    /// A sentence naming anything on this tab that is not readable as the value it stands for, or
    /// null when everything can be read.
    /// </summary>
    /// <remarks>
    /// Strictly about turning text into a value - "20:00 is not a time". Whether the value is one
    /// the shop can trade on is <c>SettingsValidation</c>'s answer, in the Application layer, and
    /// the screen shows the sentence it gets back.
    /// </remarks>
    public virtual string? Validate() => null;
}

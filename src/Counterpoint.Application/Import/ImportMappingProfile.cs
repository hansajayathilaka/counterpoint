namespace Counterpoint.Application.Import;

/// <summary>
/// A named, remembered <see cref="ImportColumnMapping"/> (SRS FR-2.22 "remembered as a named
/// profile"), so the owner maps a supplier's spreadsheet shape once and re-uses it on every later
/// delivery from the same source.
/// </summary>
/// <param name="Name">The profile's name, unique among a shop's saved profiles.</param>
/// <param name="Mapping">The column mapping this profile remembers.</param>
public sealed record ImportMappingProfile(string Name, ImportColumnMapping Mapping);

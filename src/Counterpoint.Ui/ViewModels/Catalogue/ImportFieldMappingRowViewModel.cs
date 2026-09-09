using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// One product field on the import mapping grid: which spreadsheet column (by header text) feeds
/// it (SRS FR-2.22 "map spreadsheet columns to fields").
/// </summary>
/// <remarks>
/// <see cref="HeaderChoices"/> is the same <see cref="ObservableCollection{T}"/> instance shared
/// by every row on the grid, so loading a file's headers once refreshes every row's combo box
/// (they are all bound to the same list). <see cref="NotMapped"/> is the sentinel the combo box
/// shows for "this field has no column" - <see cref="ImportColumnMapping"/> itself wants null, not
/// an empty string, so <see cref="MappedHeader"/> is what actually feeds
/// <c>ImportTabViewModel.BuildMapping</c>.
/// </remarks>
public sealed partial class ImportFieldMappingRowViewModel : ObservableObject
{
    /// <summary>Shown in the combo box for a field the current mapping leaves unmapped.</summary>
    public const string NotMapped = "(not mapped)";

    [ObservableProperty]
    private string _selectedHeader = NotMapped;

    public ImportFieldMappingRowViewModel(
        string fieldKey,
        string exportColumnName,
        string label,
        bool required,
        ObservableCollection<string> headerChoices)
    {
        ArgumentNullException.ThrowIfNull(fieldKey);
        ArgumentNullException.ThrowIfNull(exportColumnName);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(headerChoices);

        FieldKey = fieldKey;
        ExportColumnName = exportColumnName;
        Label = label;
        Required = required;
        HeaderChoices = headerChoices;
    }

    /// <summary>The <see cref="Application.Import.ImportColumnMapping"/> constructor parameter this row feeds.</summary>
    public string FieldKey { get; }

    /// <summary>
    /// The header <see cref="Application.Import.CatalogueExportColumns"/> writes for this field -
    /// what a freshly loaded file auto-maps to when its own header matches.
    /// </summary>
    public string ExportColumnName { get; }

    /// <summary>What the mapping grid shows for this row.</summary>
    public string Label { get; }

    /// <summary>True when the importer refuses a file that leaves this field unmapped.</summary>
    public bool Required { get; }

    /// <summary>
    /// The headers of whichever file is currently loaded, plus <see cref="NotMapped"/> - shared
    /// across every row so the combo box's list refreshes for all of them at once.
    /// </summary>
    public ObservableCollection<string> HeaderChoices { get; }

    /// <summary>The header this row names, or null when it is <see cref="NotMapped"/>.</summary>
    public string? MappedHeader => string.IsNullOrWhiteSpace(SelectedHeader)
        || string.Equals(SelectedHeader, NotMapped, StringComparison.Ordinal)
        ? null
        : SelectedHeader;
}

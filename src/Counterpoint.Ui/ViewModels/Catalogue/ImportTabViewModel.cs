using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Import;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The import tab of the catalogue screen: pick a spreadsheet, map its columns to product fields,
/// dry-run it, and only then commit (SRS FR-2.22, FR-2.23, AC-07, Q-08,
/// docs/03_PHASE_1_core_trading.md P1-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Commit is gated on a matching, clean dry run, not just on "a dry run happened".</b>
/// <see cref="HasCleanPreview"/> only ever becomes true inside <see cref="PreviewAsync"/>, and
/// <see cref="CommitAsync"/> re-checks the file path and the built mapping against what that
/// preview actually ran over before calling <c>ICatalogueImportService.CommitAsync</c> - changing
/// the file or a single combo box after previewing clean does not leave a stale "clean" commit
/// enabled (P1-T13 "a bad import is very hard to unwind").
/// </para>
/// <para>
/// This viewmodel never touches a file dialog: choosing where a file comes from or goes to needs
/// <c>TopLevel.StorageProvider</c>, which is a view concern the same way <c>SalesWindow</c>'s
/// scanner keystroke routing is - <see cref="Views.Catalogue.ImportTabView"/>'s code-behind asks
/// for a path and hands it to <see cref="FilePath"/> or <see cref="ExportAsync"/>.
/// </para>
/// </remarks>
public sealed partial class ImportTabViewModel : ReferenceDataTabViewModel
{
    /// <summary>
    /// Every product field a spreadsheet row can feed, in the exact order
    /// <see cref="ImportColumnMapping"/>'s constructor takes them, so <see cref="BuildMapping"/>
    /// can read <see cref="MappingRows"/> positionally.
    /// </summary>
    private static readonly (string Key, string ExportColumn, string Label, bool Required)[] FieldDefinitions =
    [
        (nameof(ImportColumnMapping.Code), CatalogueExportColumns.Code, "Code *", true),
        (nameof(ImportColumnMapping.Name), CatalogueExportColumns.Name, "Name *", true),
        (nameof(ImportColumnMapping.NameAlt), CatalogueExportColumns.NameAlt, "Alt name", false),
        (nameof(ImportColumnMapping.Category), CatalogueExportColumns.Category, "Category", false),
        (nameof(ImportColumnMapping.Brand), CatalogueExportColumns.Brand, "Brand", false),
        (nameof(ImportColumnMapping.Unit), CatalogueExportColumns.Unit, "Unit *", true),
        (nameof(ImportColumnMapping.Type), CatalogueExportColumns.Type, "Type", false),
        (nameof(ImportColumnMapping.TaxClass), CatalogueExportColumns.TaxClass, "Tax class *", true),
        (nameof(ImportColumnMapping.Location), CatalogueExportColumns.Location, "Location", false),
        (nameof(ImportColumnMapping.NonReturnable), CatalogueExportColumns.NonReturnable, "Non-returnable", false),
        (nameof(ImportColumnMapping.WarrantyDays), CatalogueExportColumns.WarrantyDays, "Warranty days", false),
        (nameof(ImportColumnMapping.Notes), CatalogueExportColumns.Notes, "Notes", false),
        (nameof(ImportColumnMapping.Barcode), CatalogueExportColumns.Barcode, "Barcode", false),
        (nameof(ImportColumnMapping.Price), CatalogueExportColumns.Price, "Price *", true),
        (nameof(ImportColumnMapping.Cost), CatalogueExportColumns.Cost, "Cost", false),
        (nameof(ImportColumnMapping.Qty), CatalogueExportColumns.Qty, "Qty on hand", false),
    ];

    private readonly ICatalogueImportService _importService;
    private readonly ISpreadsheetReader _reader;

    private string? _lastPreviewFilePath;
    private ImportColumnMapping? _lastPreviewMapping;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _countsText = string.Empty;

    [ObservableProperty]
    private bool _hasCleanPreview;

    [ObservableProperty]
    private IReadOnlyList<ImportMappingProfile> _profiles = [];

    [ObservableProperty]
    private ImportMappingProfile? _selectedProfile;

    [ObservableProperty]
    private string _profileNameToSave = string.Empty;

    public ImportTabViewModel(ICatalogueImportService importService, ISpreadsheetReader reader)
    {
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(reader);

        _importService = importService;
        _reader = reader;

        MappingRows = [.. FieldDefinitions.Select(field =>
            new ImportFieldMappingRowViewModel(field.Key, field.ExportColumn, field.Label, field.Required, HeaderChoices))];

        foreach (var row in MappingRows)
        {
            row.PropertyChanged += (_, _) => HasCleanPreview = false;
        }
    }

    /// <summary>Every spreadsheet header the currently loaded file offers, plus "(not mapped)". Shared by every mapping row.</summary>
    public ObservableCollection<string> HeaderChoices { get; } = [ImportFieldMappingRowViewModel.NotMapped];

    /// <summary>One row per product field, in <see cref="ImportColumnMapping"/> order.</summary>
    public ObservableCollection<ImportFieldMappingRowViewModel> MappingRows { get; }

    public ObservableCollection<ImportRowResultViewModel> CreateSample { get; } = [];

    public ObservableCollection<ImportRowResultViewModel> UpdateSample { get; } = [];

    public ObservableCollection<ImportRowResultViewModel> SkipSample { get; } = [];

    public ObservableCollection<ImportRowResultViewModel> ErrorSample { get; } = [];

    /// <summary>Loads the saved mapping profiles. Called once, when the catalogue screen opens.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                Profiles = await _importService.ListMappingProfilesAsync(cancellationToken).ConfigureAwait(true);
                Status = Profiles.Count == 1 ? "1 saved mapping profile." : Profiles.Count + " saved mapping profiles.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads <see cref="FilePath"/>'s headers and offers them on every row's combo box, auto-mapping
    /// any row that still says "(not mapped)" to this file's own header of the same name as the
    /// exporter would have written (so re-importing an exported file needs no manual mapping at all).
    /// </summary>
    [RelayCommand]
    public async Task LoadHeadersAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "Pick a file first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                SpreadsheetTable table;
                try
                {
                    table = await _reader.ReadAsync(FilePath, cancellationToken).ConfigureAwait(true);
                }
                catch (FileNotFoundException)
                {
                    throw new InvalidOperationException("There is no file at " + FilePath + ".");
                }
                catch (NotSupportedException exception)
                {
                    throw new InvalidOperationException(exception.Message);
                }

                HeaderChoices.Clear();
                HeaderChoices.Add(ImportFieldMappingRowViewModel.NotMapped);
                foreach (var header in table.Headers)
                {
                    HeaderChoices.Add(header);
                }

                foreach (var row in MappingRows)
                {
                    if (row.MappedHeader is not null)
                    {
                        continue;
                    }

                    var match = table.Headers.FirstOrDefault(header =>
                        string.Equals(header, row.ExportColumnName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        row.SelectedHeader = match;
                    }
                }

                Status = table.Headers.Count + " column(s) found. " + table.Rows.Count + " data row(s).";
            },
            cancellationToken).ConfigureAwait(true);

        HasCleanPreview = false;
    }

    /// <summary>Applies <see cref="SelectedProfile"/>'s remembered mapping to every row.</summary>
    [RelayCommand]
    public void ApplyProfile()
    {
        if (SelectedProfile is not { } profile)
        {
            Status = "Pick a saved profile first.";
            return;
        }

        ApplyMapping(profile.Mapping);
        Status = "Applied \"" + profile.Name + "\".";
        HasCleanPreview = false;
    }

    /// <summary>Saves the mapping currently on the grid under <see cref="ProfileNameToSave"/> (SRS FR-2.22 "remembered as a named profile").</summary>
    [RelayCommand]
    public async Task SaveProfileAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameToSave))
        {
            Status = "Name the profile first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var profile = new ImportMappingProfile(ProfileNameToSave.Trim(), BuildMapping());
                await _importService.SaveMappingProfileAsync(profile, cancellationToken).ConfigureAwait(true);
                Profiles = await _importService.ListMappingProfilesAsync(cancellationToken).ConfigureAwait(true);
                SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, profile.Name, StringComparison.Ordinal));
                Status = "Saved mapping profile \"" + profile.Name + "\".";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Removes <see cref="SelectedProfile"/> from the saved profiles.</summary>
    [RelayCommand]
    public async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is not { } profile)
        {
            Status = "Pick a saved profile first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _importService.DeleteMappingProfileAsync(profile.Name, cancellationToken).ConfigureAwait(true);
                Profiles = await _importService.ListMappingProfilesAsync(cancellationToken).ConfigureAwait(true);
                SelectedProfile = null;
                Status = "Deleted mapping profile \"" + profile.Name + "\".";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Builds the row-by-row plan and shows its counts and samples. Writes nothing (SRS FR-2.22
    /// "dry-run preview").
    /// </summary>
    [RelayCommand]
    public async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "Pick a file first.";
            return;
        }

        var mapping = BuildMapping();

        await RunAsync(
            async () =>
            {
                var report = await _importService.PreviewAsync(FilePath, mapping, cancellationToken).ConfigureAwait(true);

                CountsText = "Total " + report.Counts.TotalRows
                    + " - create " + report.Counts.Creates
                    + ", update " + report.Counts.Updates
                    + ", skip " + report.Counts.Skips
                    + ", error " + report.Counts.Errors + ".";

                Fill(CreateSample, report.CreateSample);
                Fill(UpdateSample, report.UpdateSample);
                Fill(SkipSample, report.SkipSample);
                Fill(ErrorSample, report.ErrorSample);

                _lastPreviewFilePath = FilePath;
                _lastPreviewMapping = mapping;
                HasCleanPreview = report.Counts.Errors == 0 && report.Counts.TotalRows > 0;

                Status = HasCleanPreview
                    ? "Dry run clean. Ready to commit."
                    : report.Counts.Errors > 0
                        ? "Dry run found " + report.Counts.Errors + " error row(s). Fix them and preview again."
                        : "The file has no rows to import.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Writes the plan the last clean <see cref="PreviewAsync"/> built (SRS FR-2.22 "dry run and
    /// commit produce identical counts"). Refuses if the file or the mapping has changed since.
    /// </summary>
    [RelayCommand]
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var mapping = BuildMapping();

        if (!HasCleanPreview
            || !string.Equals(_lastPreviewFilePath, FilePath, StringComparison.Ordinal)
            || _lastPreviewMapping != mapping)
        {
            Status = "Run a dry run with zero errors first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var result = await _importService.CommitAsync(FilePath, mapping, cancellationToken).ConfigureAwait(true);
                Status = "Committed: " + result.Counts.Creates + " created, " + result.Counts.Updates
                    + " updated, " + result.Counts.Skips + " skipped.";

                // A commit is a one-shot event: the plan it just wrote is no longer "not yet
                // written", so another commit needs another dry run first, even over the same
                // file and mapping.
                HasCleanPreview = false;
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Writes the full catalogue export to <paramref name="filePath"/> (SRS FR-2.23). Called
    /// directly from the view's code-behind once a save location has been chosen - exporting needs
    /// no mapping and no dry run, so it is not one of the mapping/preview/commit commands above.
    /// </summary>
    public async Task ExportAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await RunAsync(
            async () =>
            {
                await _importService.ExportCatalogueAsync(filePath, cancellationToken).ConfigureAwait(true);
                Status = "Exported the catalogue to " + filePath + ".";
            },
            cancellationToken).ConfigureAwait(true);
    }

    private ImportColumnMapping BuildMapping()
    {
        string? Value(string key) => MappingRows.First(row => row.FieldKey == key).MappedHeader;

        return new ImportColumnMapping(
            Code: Value(nameof(ImportColumnMapping.Code)),
            Name: Value(nameof(ImportColumnMapping.Name)),
            NameAlt: Value(nameof(ImportColumnMapping.NameAlt)),
            Category: Value(nameof(ImportColumnMapping.Category)),
            Brand: Value(nameof(ImportColumnMapping.Brand)),
            Unit: Value(nameof(ImportColumnMapping.Unit)),
            Type: Value(nameof(ImportColumnMapping.Type)),
            TaxClass: Value(nameof(ImportColumnMapping.TaxClass)),
            Location: Value(nameof(ImportColumnMapping.Location)),
            NonReturnable: Value(nameof(ImportColumnMapping.NonReturnable)),
            WarrantyDays: Value(nameof(ImportColumnMapping.WarrantyDays)),
            Notes: Value(nameof(ImportColumnMapping.Notes)),
            Barcode: Value(nameof(ImportColumnMapping.Barcode)),
            Price: Value(nameof(ImportColumnMapping.Price)),
            Cost: Value(nameof(ImportColumnMapping.Cost)),
            Qty: Value(nameof(ImportColumnMapping.Qty)));
    }

    private void ApplyMapping(ImportColumnMapping mapping)
    {
        void Set(string key, string? header) =>
            MappingRows.First(row => row.FieldKey == key).SelectedHeader = header ?? ImportFieldMappingRowViewModel.NotMapped;

        Set(nameof(ImportColumnMapping.Code), mapping.Code);
        Set(nameof(ImportColumnMapping.Name), mapping.Name);
        Set(nameof(ImportColumnMapping.NameAlt), mapping.NameAlt);
        Set(nameof(ImportColumnMapping.Category), mapping.Category);
        Set(nameof(ImportColumnMapping.Brand), mapping.Brand);
        Set(nameof(ImportColumnMapping.Unit), mapping.Unit);
        Set(nameof(ImportColumnMapping.Type), mapping.Type);
        Set(nameof(ImportColumnMapping.TaxClass), mapping.TaxClass);
        Set(nameof(ImportColumnMapping.Location), mapping.Location);
        Set(nameof(ImportColumnMapping.NonReturnable), mapping.NonReturnable);
        Set(nameof(ImportColumnMapping.WarrantyDays), mapping.WarrantyDays);
        Set(nameof(ImportColumnMapping.Notes), mapping.Notes);
        Set(nameof(ImportColumnMapping.Barcode), mapping.Barcode);
        Set(nameof(ImportColumnMapping.Price), mapping.Price);
        Set(nameof(ImportColumnMapping.Cost), mapping.Cost);
        Set(nameof(ImportColumnMapping.Qty), mapping.Qty);
    }

    private static void Fill(ObservableCollection<ImportRowResultViewModel> target, IReadOnlyList<ImportRowResult> source)
    {
        target.Clear();
        foreach (var row in source)
        {
            target.Add(new ImportRowResultViewModel(row));
        }
    }

    partial void OnFilePathChanged(string value) => HasCleanPreview = false;
}

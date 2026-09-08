namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// One choice in a reference-data picker - category, brand, unit or tax class - on the product
/// editor (SRS FR-2.1-FR-2.8). <see cref="Id"/> is null only for "(none)", offered where the
/// field itself is optional.
/// </summary>
public sealed record PickerOption(long? Id, string Name)
{
    public override string ToString() => Name;
}

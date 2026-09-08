namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// One choice in the "parent category" picker. <see cref="Id"/> is null for "(top level)" -
/// FR-2.20's two-level rule means only a top-level category may ever be offered as a parent.
/// </summary>
public sealed record CategoryParentOption(long? Id, string Name)
{
    public override string ToString() => Name;
}

using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the category list (SRS FR-2.20).</summary>
public sealed class CategoryRowViewModel
{
    public CategoryRowViewModel(CategoryRecord category)
    {
        ArgumentNullException.ThrowIfNull(category);

        Id = category.Id;
        Name = category.Name;
        ParentId = category.ParentId;
        ParentText = category.ParentId is null ? "(top level)" : category.ParentName ?? string.Empty;
        Active = category.Active;
        StateText = category.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public long? ParentId { get; }

    public string ParentText { get; }

    public bool Active { get; }

    public string StateText { get; }
}

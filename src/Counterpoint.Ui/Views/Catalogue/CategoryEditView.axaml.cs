using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The category create/edit dialog content. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Catalogue.CategoryEditViewModel"/>.
/// </summary>
public partial class CategoryEditView : UserControl
{
    public CategoryEditView()
    {
        InitializeComponent();
    }
}

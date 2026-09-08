using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The category tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.CategoryTabViewModel"/>.
/// </summary>
public partial class CategoryTabView : UserControl
{
    public CategoryTabView()
    {
        InitializeComponent();
    }
}

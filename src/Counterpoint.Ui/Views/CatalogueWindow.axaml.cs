using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The owner's catalogue reference-data screen. Markup and nothing else: every behaviour it has
/// is a binding to <see cref="ViewModels.Catalogue.CatalogueViewModel"/>.
/// </summary>
public partial class CatalogueWindow : Window
{
    public CatalogueWindow()
    {
        InitializeComponent();
    }
}

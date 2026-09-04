using CommunityToolkit.Mvvm.ComponentModel;

namespace Counterpoint.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Placeholder shell content. The real sales screen arrives in P0-T06.
    [ObservableProperty]
    private string _greeting = "Counterpoint";
}

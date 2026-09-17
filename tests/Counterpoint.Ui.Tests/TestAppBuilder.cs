using Avalonia;
using Avalonia.Headless;
using Counterpoint.Ui.Tests;

// Tells Avalonia.Headless.XUnit which AppBuilder to use for every [AvaloniaFact]/[AvaloniaTheory]
// in this assembly: Counterpoint.Ui's own App (App.axaml), the exact FluentTheme + P3-T10 token
// dictionaries the shipped application runs under - never a bare test-only stub - so a view under
// test resolves the same DynamicResource brushes it would in Counterpoint.App.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Counterpoint.Ui.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Counterpoint.Ui.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

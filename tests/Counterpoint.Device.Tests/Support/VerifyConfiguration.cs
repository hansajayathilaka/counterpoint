using System.Runtime.CompilerServices;
using DiffEngine;

namespace Counterpoint.Device.Tests.Support;

/// <summary>
/// Snapshot test settings, applied once per test run.
/// </summary>
internal static class VerifyConfiguration
{
    /// <summary>
    /// Stops Verify launching a diff tool when a snapshot changes. A failing snapshot must
    /// print a diff and fail, in CI and on a developer machine alike - not open a window.
    /// </summary>
    [ModuleInitializer]
    public static void Initialise() => DiffRunner.Disabled = true;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Counterpoint.Integration.Tests.Performance;

/// <summary>
/// Reads <c>docs/perf-regression-baseline.json</c> - the recorded timings
/// <see cref="PerformanceRegressionGuardTests"/> compares a fresh measurement against, and the
/// same file <c>/perf-gate</c> reads outside the shop terminal (docs/09_HARDWARE_INTEGRATION.md).
/// </summary>
/// <remarks>
/// <b>Not <c>docs/perf-baseline.md</c>'s NFR-P1...P7 table.</b> That table's Budget/Measured
/// columns stay "—" until <c>HW-T07</c> populates them on the shop's own terminal - a figure
/// measured anywhere else is not a figure for that table. This file is a separate, deliberately
/// machine-relative record: whatever this regression guard last measured on whichever machine ran
/// it, kept only to catch a sudden >20% drift between commits. Updating it is a deliberate,
/// reviewed act - this class never writes it.
/// </remarks>
internal sealed class PerformanceBaseline
{
    private readonly IReadOnlyDictionary<string, double> _millisecondsByOperation;

    private PerformanceBaseline(IReadOnlyDictionary<string, double> millisecondsByOperation)
    {
        _millisecondsByOperation = millisecondsByOperation;
    }

    /// <summary>The recorded milliseconds for <paramref name="operation"/> (for example <c>"NFR-P1"</c>).</summary>
    /// <exception cref="InvalidOperationException">The baseline file has no entry for this operation.</exception>
    internal double MillisecondsFor(string operation) =>
        _millisecondsByOperation.TryGetValue(operation, out var ms)
            ? ms
            : throw new InvalidOperationException(
                "docs/perf-regression-baseline.json has no recorded figure for '" + operation + "'. "
                + "Run the harness once, record what it measures, and commit that as the baseline.");

    internal static async Task<PerformanceBaseline> LoadAsync()
    {
        var path = FindBaselineFile();

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);

        var operations = document.RootElement.GetProperty("operations");
        var millisecondsByOperation = operations
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetProperty("ms").GetDouble(),
                StringComparer.Ordinal);

        return new PerformanceBaseline(millisecondsByOperation);
    }

    /// <summary>
    /// Walks up from the test binary's own directory to the repository root, the same way
    /// <c>ArchitectureTests.RepositoryRoot</c> does, so this works whether the tests run from
    /// <c>dotnet test</c> or from an IDE's own output path.
    /// </summary>
    private static string FindBaselineFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "perf-regression-baseline.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "docs/perf-regression-baseline.json was not found above " + AppContext.BaseDirectory
            + ". The performance regression guard has nothing to compare against.");
    }
}

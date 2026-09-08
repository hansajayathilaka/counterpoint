using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Settings;

/// <summary>
/// P1-T03's last "Done when": <b>no hard-coded tax rate, discount limit or currency symbol
/// anywhere</b>. This is that grep test (SRS FR-10.2, FR-10.3, FR-10.5, NFR-M1).
/// </summary>
/// <remarks>
/// <para>
/// It reads the source under <c>src/</c> rather than the compiled assemblies, because the thing
/// being policed is a literal a developer typed. Comment lines are skipped: a doc comment
/// explaining that the shop trades in rupees is documentation, and the rule is about values the
/// program acts on.
/// </para>
/// <para>
/// <c>SettingDefaults.cs</c> is the one exemption, by design. The defaults have to be somewhere,
/// and having exactly one file where they may be is what makes every other file's silence
/// meaningful.
/// </para>
/// </remarks>
public sealed class NoHardCodedBusinessValuesTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    /// <summary>The single file allowed to name a currency, a rate or a limit.</summary>
    private const string DefaultsFileName = "SettingDefaults.cs";

    /// <summary>
    /// Currency names and symbols. <c>$</c> is deliberately not on the list: it is SQLite's
    /// parameter prefix, so <c>"WHERE id = $id"</c> would be a false positive on every repository
    /// in the solution.
    /// </summary>
    private static readonly string[] CurrencyTokens =
        ["LKR", "USD", "EUR", "GBP", "Rs.", "₨", "€", "£", "¥"];

    /// <summary>A string literal, with escapes, as it appears in C# or XAML.</summary>
    private static readonly Regex StringLiteral = new(
        "\"(?:[^\"\\\\]|\\\\.)*\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A tax rate built from a number written in the source. <c>\s</c> already matches a newline,
    /// so this tolerates the call being wrapped across lines, e.g.
    /// <c>TaxRate.FromPercent(\n 15m)</c> - as long as it is run against the whole file's code,
    /// not line by line (see <see cref="FindMatches"/>).
    /// </summary>
    private static readonly Regex LiteralTaxRate = new(
        @"TaxRate\.From(?:Percent|Fraction)\s*\(\s*(?<value>-?[0-9][0-9_]*(?:\.[0-9]+)?)m?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>A discount or fee limit built from a number written in the source. See <see cref="LiteralTaxRate"/> for why <c>\s</c> is enough to span lines.</summary>
    private static readonly Regex LiteralPercentage = new(
        @"Percentage\.From(?:Percent|Fraction)\s*\(\s*(?<value>-?[0-9][0-9_]*(?:\.[0-9]+)?)m?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>A rounding policy built with a decimal-place count written in the source. See <see cref="LiteralTaxRate"/> for why <c>\s</c> is enough to span lines.</summary>
    private static readonly Regex ConstructedRoundingPolicy = new(
        @"new\s+Half(?:AwayFromZero|ToEven)Rounding\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void FR_10_2_NoCurrencySymbolIsWrittenIntoTheSource()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (IsDefaults(file))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                foreach (Match literal in StringLiteral.Matches(line))
                {
                    offenders.AddRange(CurrencyTokens
                        .Where(token => literal.Value.Contains(token, StringComparison.Ordinal))
                        .Select(token => Describe(file, number, $"currency token \"{token}\"")));
                }
            }
        }

        offenders.Should().BeEmpty(
            "the currency symbol, its position and its decimal places are settings the shop owns "
            + "(FR-10.2). The only file allowed to name one is " + DefaultsFileName
            + ". Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void FR_10_3_NoTaxRateIsWrittenIntoTheSource()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (IsDefaults(file))
            {
                continue;
            }

            offenders.AddRange(FindMatches(
                file,
                LiteralTaxRate,
                match => "tax rate " + match.Groups["value"].Value));
        }

        offenders.Should().BeEmpty(
            "tax rates live in tax_class rows chosen at first run, never in code - the regime is "
            + "a data decision (FR-10.3, Q-02). Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void FR_10_5_NoDiscountOrFeeLimitIsWrittenIntoTheSource()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (IsDefaults(file))
            {
                continue;
            }

            offenders.AddRange(FindMatches(
                file,
                LiteralPercentage,
                match => "rate " + match.Groups["value"].Value));
        }

        offenders.Should().BeEmpty(
            "the cashier's discount ceiling and the restocking fee are settings (FR-10.5, Q-12). "
            + "Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void FR_10_2_NoRoundingPolicyIsBuiltOutsideTheDomain()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (IsIn(file, "Counterpoint.Domain"))
            {
                // RoundingPolicyFactory is the one place a rule becomes a policy, and it lives
                // in the Domain beside the policies it builds.
                continue;
            }

            offenders.AddRange(FindMatches(
                file,
                ConstructedRoundingPolicy,
                _ => "a rounding policy built by hand"));
        }

        offenders.Should().BeEmpty(
            "the decimal places and the rounding rule come from settings through "
            + "RoundingPolicyFactory and SettingsRoundingPolicy, so a change takes effect without "
            + "a restart (FR-10.2). Offenders: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The blind spot this test class had before this fix: a call is only an "offender" to the
    /// original, line-by-line version of these checks if the whole thing - method name, paren
    /// and literal - sits on one line. Wrap the same call across two lines and it used to slip
    /// through undetected. These regression tests write a synthetic file to a temp directory and
    /// scan it with the real <see cref="FindMatches"/> plumbing to prove that no longer happens.
    /// </summary>
    public sealed class MultiLineOffendersAreStillCaught : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "counterpoint-hardcoded-values-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void AMultiLineTaxRateCallIsCaught()
        {
            var file = WriteFile(
                "MultiLineTaxRate.cs",
                """
                internal sealed class Offender
                {
                    private static readonly TaxRate Rate = TaxRate.FromPercent(
                        15m);
                }
                """);

            var offenders = FindMatches(
                file,
                LiteralTaxRate,
                match => "tax rate " + match.Groups["value"].Value).ToList();

            offenders.Should().ContainSingle()
                .Which.Should().Be("MultiLineTaxRate.cs:3 tax rate 15");
        }

        [Fact]
        public void AMultiLineDiscountPercentageCallIsCaught()
        {
            var file = WriteFile(
                "MultiLinePercentage.cs",
                """
                internal sealed class Offender
                {
                    private static readonly Percentage Limit = Percentage.FromPercent(
                        20m);
                }
                """);

            var offenders = FindMatches(
                file,
                LiteralPercentage,
                match => "rate " + match.Groups["value"].Value).ToList();

            offenders.Should().ContainSingle()
                .Which.Should().Be("MultiLinePercentage.cs:3 rate 20");
        }

        [Fact]
        public void AMultiLineRoundingPolicyConstructorCallIsCaught()
        {
            var file = WriteFile(
                "MultiLineRounding.cs",
                """
                internal sealed class Offender
                {
                    private static readonly IRoundingPolicy Policy = new HalfAwayFromZeroRounding(
                        2);
                }
                """);

            var offenders = FindMatches(
                file,
                ConstructedRoundingPolicy,
                _ => "a rounding policy built by hand").ToList();

            offenders.Should().ContainSingle()
                .Which.Should().Be("MultiLineRounding.cs:3 a rounding policy built by hand");
        }

        [Fact]
        public void ASingleLineOffenderIsStillCaughtOnItsOwnLine()
        {
            var file = WriteFile(
                "SingleLineTaxRate.cs",
                """
                internal sealed class Offender
                {
                    private static readonly TaxRate Rate = TaxRate.FromPercent(15m);
                }
                """);

            var offenders = FindMatches(
                file,
                LiteralTaxRate,
                match => "tax rate " + match.Groups["value"].Value).ToList();

            offenders.Should().ContainSingle()
                .Which.Should().Be("SingleLineTaxRate.cs:3 tax rate 15");
        }

        private FileInfo WriteFile(string name, string content)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, name);
            File.WriteAllText(path, content);
            return new FileInfo(path);
        }
    }

    private static bool IsDefaults(FileInfo file) =>
        file.Name.Equals(DefaultsFileName, StringComparison.Ordinal);

    private static bool IsIn(FileInfo file, string projectName) =>
        file.FullName.Contains(
            Path.DirectorySeparatorChar + projectName + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

    /// <summary>
    /// The lines of a file that are code. A line whose first non-space characters open a comment
    /// is prose, and prose is allowed to say "the shop trades in rupees".
    /// </summary>
    private static IEnumerable<(string Line, int Number)> CodeLines(FileInfo file)
    {
        var number = 0;

        foreach (var line in File.ReadLines(file.FullName))
        {
            number++;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith('*')
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (line, number);
        }
    }

    /// <summary>
    /// Runs <paramref name="regex"/> against a file's code as a single joined string, rather than
    /// line by line, so a call split across lines (e.g. <c>TaxRate.FromPercent(\n  15m)</c>) is
    /// still caught. Comment lines are left out exactly as <see cref="CodeLines"/> already leaves
    /// them out. Each match is reported against the original source line it starts on, computed
    /// by counting the newlines that precede it in the joined text.
    /// </summary>
    private static IEnumerable<string> FindMatches(
        FileInfo file, Regex regex, Func<Match, string> what)
    {
        var lines = CodeLines(file).ToArray();

        if (lines.Length == 0)
        {
            yield break;
        }

        var joined = string.Join('\n', lines.Select(codeLine => codeLine.Line));

        foreach (Match match in regex.Matches(joined))
        {
            var row = CountNewlinesBefore(joined, match.Index);
            var number = lines[Math.Min(row, lines.Length - 1)].Number;

            yield return Describe(file, number, what(match));
        }
    }

    /// <summary>The number of '\n' characters in <paramref name="text"/> before <paramref name="index"/> - the row, within the joined code lines, that a match index falls on.</summary>
    private static int CountNewlinesBefore(string text, int index)
    {
        var count = 0;

        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static string Describe(FileInfo file, int line, string what) =>
        string.Create(CultureInfo.InvariantCulture, $"{file.Name}:{line} {what}");

    /// <summary>Every hand-written source file under <c>src/</c>.</summary>
    private static FileInfo[] SourceFiles()
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"));

        source.Exists.Should().BeTrue("the src directory must exist at {0}", source.FullName);

        var files = source.GetFiles("*.cs", SearchOption.AllDirectories)
            .Concat(source.GetFiles("*.axaml", SearchOption.AllDirectories))
            .Where(file => !IsGenerated(file))
            .ToArray();

        files.Should().NotBeEmpty("there must be source to police");

        return files;
    }

    /// <summary>
    /// Build output and generated code. <c>CompiledModels</c> is EF Core's generated model - it
    /// is regenerated from the schema, and nothing in it is a value a developer chose.
    /// </summary>
    private static bool IsGenerated(FileInfo file) =>
        file.FullName.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || file.FullName.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || file.FullName.Contains(Path.DirectorySeparatorChar + "CompiledModels" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || file.Name.EndsWith(".Designer.cs", StringComparison.Ordinal)
        || file.Name.EndsWith(".g.cs", StringComparison.Ordinal);

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}

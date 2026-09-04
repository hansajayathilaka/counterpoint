using System;

namespace Counterpoint.Domain.Tests.Support;

/// <summary>
/// Seeded pseudo-random input for the property tests P0-T02 requires.
///
/// Why this and not FsCheck (which <c>Directory.Packages.props</c> already pins)?
/// The two properties here are specified by count and by range — ten thousand decimals
/// across the whole storable money range, and line sets of one to fifty lines. Expressing
/// that in FsCheck means a custom <c>Arbitrary</c> plus a <c>MaxTest</c> override, which is
/// more machinery than the loop it replaces and still does not state the range as plainly.
/// A fixed seed makes every failure reproducible, and the seed is printed in the assertion
/// message so a red build can be re-run exactly. It also keeps Counterpoint.Domain.Tests on
/// the package set P0-T01 pinned, with no new dependency to justify in docs/adr/.
///
/// <see cref="System.Random.NextDouble"/> is never used: binary floating point is banned
/// across this codebase (CLAUDE.md invariant 1) and a test helper is no exception. Every
/// value comes from <see cref="System.Random.NextInt64(long, long)"/> and becomes a decimal
/// by exact integer division.
/// </summary>
internal sealed class DeterministicSample
{
    private readonly Random _random;

    internal DeterministicSample(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    /// <summary>The seed, so a failing assertion can name it.</summary>
    internal int Seed { get; }

    /// <summary>A scaled integer anywhere in the storable range.</summary>
    internal long NextScaled() => _random.NextInt64(long.MinValue, long.MaxValue);

    /// <summary>A scaled integer in <c>[minInclusive, maxExclusive)</c>.</summary>
    internal long NextScaled(long minInclusive, long maxExclusive) =>
        _random.NextInt64(minInclusive, maxExclusive);

    /// <summary>An integer in <c>[minInclusive, maxExclusive)</c>.</summary>
    internal int NextInt(int minInclusive, int maxExclusive) =>
        _random.Next(minInclusive, maxExclusive);

    /// <summary>
    /// A decimal anywhere in the storable range, carrying exactly the four fractional digits
    /// the storage scale holds.
    /// </summary>
    internal decimal NextStorableDecimal() => NextScaled() / 10_000m;

    /// <summary>
    /// A decimal in <c>[minInclusive, maxExclusive)</c> currency units, to four fractional digits.
    /// </summary>
    internal decimal NextStorableDecimal(decimal minInclusive, decimal maxExclusive) =>
        NextScaled((long)(minInclusive * 10_000m), (long)(maxExclusive * 10_000m)) / 10_000m;

    /// <summary>
    /// A decimal with more fractional digits than the storage scale can hold, so the
    /// quantisation in <c>ToScaled()</c> has something to do.
    /// </summary>
    internal decimal NextOverPreciseDecimal()
    {
        var whole = NextScaled(-10_000_000_000_000L, 10_000_000_000_000L) / 10_000m;
        var extraDigits = NextScaled(0L, 10_000L) / 100_000_000m;

        return whole + (whole < 0m ? -extraDigits : extraDigits);
    }
}

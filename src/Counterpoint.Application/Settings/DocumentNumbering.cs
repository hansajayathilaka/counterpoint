namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.4 - the prefix, pattern and starting number of one document series.
/// </summary>
/// <param name="Prefix">The literal that <c>{prefix}</c> expands to, for example <c>INV-</c>.</param>
/// <param name="Pattern">
/// The shape of the number, for example <c>{prefix}{yyyy}-{n:000000}</c>. Stored on the
/// <c>number_sequence</c> row the allocator reads.
/// </param>
/// <param name="StartingNumber">
/// The first number the series issues. Applied to <c>number_sequence.next_val</c> at first run
/// only: once a series has issued a number, moving it would break the gapless guarantee
/// (CLAUDE.md invariant 4, AC-19).
/// </param>
public sealed record DocumentNumbering(string Prefix, string Pattern, long StartingNumber);

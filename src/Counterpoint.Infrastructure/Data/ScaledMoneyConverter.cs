using Counterpoint.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Stores a <see cref="Money"/> as the <c>INTEGER</c> scaled ×10 000 that
/// docs/01_DATA_MODEL.md §1 requires (DM-01, CLAUDE.md invariant 1).
/// </summary>
/// <remarks>
/// <para>
/// The scaling itself lives in <see cref="Money.ToScaled"/> and <see cref="Money.FromScaled"/>,
/// in <c>Counterpoint.Domain</c>, so the database and the arithmetic cannot drift apart on the
/// rounding rule or the overflow boundary. This class only says "that is how the column is
/// written".
/// </para>
/// <para>
/// Applied once as a convention in <see cref="PosDbContext.ConfigureConventions"/>, and to
/// <c>Money?</c> as well, so no money column can be missed. A bare <see cref="decimal"/> property
/// would map to <c>TEXT</c> with no error to show for it, and money stored as text does not add
/// up: docs/01_DATA_MODEL.md §13 bans one, and this is what replaces it.
/// </para>
/// </remarks>
public sealed class ScaledMoneyConverter : ValueConverter<Money, long>
{
    public ScaledMoneyConverter()
        : base(
            money => money.ToScaled(),
            scaled => Money.FromScaled(scaled))
    {
    }

    /// <summary>Shared instance; the converter holds no state.</summary>
    public static ScaledMoneyConverter Instance { get; } = new();
}

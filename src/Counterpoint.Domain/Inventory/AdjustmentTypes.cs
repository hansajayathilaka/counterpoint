using System;
using System.Globalization;

namespace Counterpoint.Domain.Inventory;

/// <summary>
/// What an <see cref="AdjustmentType"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §4). The same shape as <c>Counterpoint.Domain.Returns.ReturnDispositions</c>:
/// the two tokens are exactly the ones <c>ck_stock_movement_movement_type</c> already allows -
/// this task adds no new movement type, it is the first thing that ever posts these two - so they
/// are spelled out once here rather than left for every adapter to spell separately.
/// </summary>
public static class AdjustmentTypes
{
    /// <summary>The <c>stock_movement.movement_type</c> value for <see cref="AdjustmentType.Adjustment"/>.</summary>
    public const string AdjustmentToken = "ADJUSTMENT";

    /// <summary>The <c>stock_movement.movement_type</c> value for <see cref="AdjustmentType.Damage"/>.</summary>
    public const string DamageToken = "DAMAGE";

    /// <summary>The database token for an adjustment type.</summary>
    public static string ToToken(AdjustmentType type) => type switch
    {
        AdjustmentType.Adjustment => AdjustmentToken,
        AdjustmentType.Damage => DamageToken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "There are exactly two adjustment movement types."),
    };

    /// <summary>Reads a database token back into an adjustment type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is neither <see cref="AdjustmentToken"/> nor <see cref="DamageToken"/>.
    /// </exception>
    public static AdjustmentType Parse(string token) => token switch
    {
        AdjustmentToken => AdjustmentType.Adjustment,
        DamageToken => AdjustmentType.Damage,
        _ => throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{token}' is not an adjustment movement type. It must be '{AdjustmentToken}' or '{DamageToken}'.")),
    };
}

namespace Counterpoint.Domain.Cash;

/// <summary>
/// Which way money moved across the drawer (docs/01_DATA_MODEL.md §7, <c>cash_movement.direction</c>,
/// SRS FR-8.2).
/// </summary>
/// <remarks>
/// The sign always lives here, never in the amount - <c>cash_movement.amount</c> is
/// <c>CHECK (amount &gt; 0)</c>, so a negative "in" can never net silently out of a Z report
/// (the same reasoning <c>CashMovementConfiguration</c>'s own remarks give for the database side
/// of this rule).
/// </remarks>
public enum CashMovementDirection
{
    /// <summary>Money added to the drawer - a float top-up or an owner deposit (SRS FR-8.2).</summary>
    In = 0,

    /// <summary>Money taken out of the drawer - a petty expense, a supplier payment or banking (SRS FR-8.2).</summary>
    Out = 1,
}

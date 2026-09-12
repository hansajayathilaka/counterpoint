namespace Counterpoint.Domain.Returns;

/// <summary>
/// Whether a return may proceed, and why (SRS FR-5, BR-*, FR-10.5, task P2-T01 step 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Never a bare boolean.</b> A cashier screen has to show the reason a line cannot be
/// returned, and an audit row has to record it - a <c>bool</c> answers neither. The three cases
/// are closed (the same shape <c>Counterpoint.Devices.Printing.ReceiptNode</c> uses): a caller
/// pattern-matches on
/// <see cref="Allowed"/>, <see cref="AllowedWithOverride"/> or <see cref="Denied"/> and there is
/// no fourth thing it could be.
/// </para>
/// <para>
/// <b><see cref="Denied"/> carries no override machinery of any kind</b> - not a token, not an
/// action name, nothing a caller could feed to <c>IOwnerOverrideService</c>. That is what makes
/// AC-06 (cumulative over-return is never overridable) structurally true rather than a
/// convention: <see cref="Counterpoint.Domain.Returns.ReturnPolicy.EvaluateCumulativeQuantity"/>
/// only ever returns <see cref="Allowed"/> or <see cref="Denied"/>, so there is no code path
/// anywhere that could turn its "no" into a "yes, with permission" - the type itself does not
/// offer one.
/// </para>
/// </remarks>
public abstract record ReturnEligibility
{
    private ReturnEligibility()
    {
    }

    /// <summary>Nothing stands in the way of this return.</summary>
    public sealed record Allowed : ReturnEligibility
    {
        /// <summary>The one instance - there is nothing to distinguish two of these.</summary>
        public static readonly Allowed Instance = new();
    }

    /// <summary>
    /// The return is against policy, but the owner may authorise it anyway (SRS FR-5.6, FR-5.10,
    /// AC-05). A caller obtains an <c>OverrideToken</c> from <c>IOwnerOverrideService</c> naming
    /// the action this rule corresponds to, and only a token that is spent for that action lets
    /// the return proceed.
    /// </summary>
    /// <param name="Reason">Why, in words a cashier and an auditor both read the same way.</param>
    public sealed record AllowedWithOverride(string Reason) : ReturnEligibility;

    /// <summary>
    /// The return may not proceed. No override exists for this case anywhere in the codebase -
    /// see the remarks on <see cref="ReturnEligibility"/> for why that is true by construction,
    /// not by omission.
    /// </summary>
    /// <param name="Reason">Why, in words a cashier and an auditor both read the same way.</param>
    public sealed record Denied(string Reason) : ReturnEligibility;
}

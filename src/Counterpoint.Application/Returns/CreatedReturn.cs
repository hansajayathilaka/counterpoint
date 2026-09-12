using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>What <see cref="ICreateReturn.CreateAsync"/> hands back once the return has committed.</summary>
/// <param name="SaleReturnId">The new <c>sale_return</c> row.</param>
/// <param name="ReturnNo">The allocated return number.</param>
/// <param name="TotalRefund">What was actually refunded, after the restocking fee.</param>
/// <param name="PrintJobId">The queued receipt - never printed here (CLAUDE.md invariant 7).</param>
public sealed record CreatedReturn(long SaleReturnId, string ReturnNo, Money TotalRefund, long PrintJobId);

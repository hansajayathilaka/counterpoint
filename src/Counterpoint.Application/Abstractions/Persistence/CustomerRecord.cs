using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of <c>customer</c> (docs/01_DATA_MODEL.md §5, FR-6.1). <c>Balance</c> is a projection
/// of the account ledger that arrives with credit accounts in P5-T02; here it is always zero.
/// </summary>
public sealed record CustomerRecord(
    long Id,
    string Name,
    string? Phone,
    string? Address,
    string? TaxNo,
    string Type,
    Money CreditLimit,
    Money Balance,
    bool Active);

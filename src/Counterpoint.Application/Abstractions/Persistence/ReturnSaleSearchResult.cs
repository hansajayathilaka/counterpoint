using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of a bill search by date, customer or amount (SRS FR-5.1, task P2-T02 step 1).</summary>
/// <param name="SaleId">Pass this to <see cref="IReturnableSaleLookup.FindBySaleIdAsync"/> to open it.</param>
/// <param name="BillNo">Shown to the cashier to confirm the right bill was found.</param>
/// <param name="SoldAt">When the bill was completed.</param>
/// <param name="CustomerName">The named customer, or <c>Walk-in</c> for an anonymous sale.</param>
/// <param name="Total">What the bill came to.</param>
public sealed record ReturnSaleSearchResult(
    long SaleId,
    string BillNo,
    DateTimeOffset SoldAt,
    string CustomerName,
    Money Total);

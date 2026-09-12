using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Finds a bill to return against without its number - by date, customer or amount (SRS FR-5.1,
/// task P2-T02 step 1). Every field is optional and narrows the search further; a search with
/// every field null is refused rather than returning the whole sales history.
/// </summary>
/// <param name="FromDate">The earliest business date to search, inclusive.</param>
/// <param name="ToDate">The latest business date to search, inclusive.</param>
/// <param name="CustomerName">A fragment of the customer's name, matched case-insensitively.</param>
/// <param name="Amount">The bill total to search near.</param>
/// <param name="AmountTolerance">
/// How far <see cref="Amount"/> may be off and still match - a customer rarely remembers a bill
/// to the last cent. Ignored unless <see cref="Amount"/> is given.
/// </param>
public sealed record ReturnSaleSearchCriteria(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? CustomerName = null,
    Money? Amount = null,
    Money? AmountTolerance = null);

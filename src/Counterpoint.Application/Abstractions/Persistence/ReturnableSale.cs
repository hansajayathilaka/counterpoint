using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A completed bill, reassembled exactly as a linked return needs it (task P2-T02, SRS FR-5).
/// </summary>
/// <param name="SaleId">The original bill.</param>
/// <param name="BillNo">
/// What is scanned or typed to find this bill (SRS FR-5.1) - the same text a receipt's own
/// barcode encodes, so scanning it and typing it land on the same lookup.
/// </param>
/// <param name="SoldAt">When the bill was completed - what the return window (SRS FR-5.6) is measured from.</param>
/// <param name="BusinessDate">The trading day the bill belongs to.</param>
/// <param name="Status">The bill's current status. A cancelled bill has nothing left to return.</param>
/// <param name="CustomerId">The customer the original sale carried, or null - inherited onto the return.</param>
/// <param name="Total">What the bill came to.</param>
/// <param name="Lines">Every line on the bill, in order.</param>
public sealed record ReturnableSale(
    long SaleId,
    string BillNo,
    DateTimeOffset SoldAt,
    DateOnly BusinessDate,
    string Status,
    long? CustomerId,
    Money Total,
    IReadOnlyList<ReturnableSaleLine> Lines);

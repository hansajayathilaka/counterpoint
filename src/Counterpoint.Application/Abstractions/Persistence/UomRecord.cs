namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of <c>uom</c> (docs/01_DATA_MODEL.md §3).
/// </summary>
/// <remarks>
/// <b>There is no <c>Active</c> here, because <c>uom</c> has no <c>active</c> column.</b> Unlike
/// <c>category</c>, <c>brand</c>, <c>tax_class</c>, <c>supplier</c> and <c>customer</c>, the
/// current schema (<c>Skeleton0001</c>, P0-T04) never gave units of measure one, and P1-T04's own
/// "Do this" list keeps schema work out of scope. A unit already in use can therefore only be
/// renamed or deleted (deletion refused while a product references it); it cannot be turned off.
/// See the note on <see cref="ICatalogueReferenceDataSeed"/> and the P1-T04 task report for the
/// follow-up this leaves behind.
/// </remarks>
public sealed record UomRecord(long Id, string Name, string Symbol, int DecimalPlaces);

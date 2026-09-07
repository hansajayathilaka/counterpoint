namespace Counterpoint.Application.Sales;

/// <summary>
/// The <c>payment.tender_type</c> values the database will accept (docs/01_DATA_MODEL.md §10).
/// </summary>
/// <remarks>
/// Constants, not an enum, and only the one the skeleton tenders. The full <c>TenderType</c>
/// enum in <c>Domain/Enums</c> - mirrored against the CHECK constraint by an integration test -
/// is P1-T04's, with the rest of the reference data. Inventing it here would leave two sources
/// of truth for the same list.
/// </remarks>
public static class TenderTypes
{
    /// <summary>Notes and coins. The only tender the walking skeleton takes.</summary>
    public const string Cash = "CASH";
}

namespace Counterpoint.Domain.Returns;

/// <summary>
/// What happens to a returned unit's stock (docs/01_DATA_MODEL.md §6, SRS FR-5.8, BR-06). The
/// operator must choose one explicitly for every returned line - FR-5.8 is clear that this must
/// never be silently defaulted, which is why there is no third, "unspecified" member here for a
/// caller to fall back on.
/// </summary>
public enum ReturnDisposition
{
    /// <summary>Fit to sell again - restocked (SRS FR-5.8's "return to sellable stock").</summary>
    Sellable = 0,

    /// <summary>
    /// Faulty or damaged - not added back to sellable stock (SRS FR-5.8's "quarantine as
    /// damaged/faulty").
    /// </summary>
    Damaged = 1,
}

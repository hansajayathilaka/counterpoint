namespace Counterpoint.Application.Settings;

/// <summary>Where the currency symbol sits against the amount (SRS FR-10.2).</summary>
public enum CurrencySymbolPosition
{
    /// <summary><c>Rs. 1,250.00</c>.</summary>
    Before = 0,

    /// <summary><c>1,250.00 Rs.</c>.</summary>
    After = 1,
}

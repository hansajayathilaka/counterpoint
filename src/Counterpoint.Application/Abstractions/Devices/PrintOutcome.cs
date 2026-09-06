using System;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// What happened when a document was sent to a printer (SRS FR-7.8).
///
/// A failure is a value here, not an exception, because failing to print is an ordinary,
/// expected event in a shop: the paper runs out mid-afternoon and the sale still has to
/// complete. The caller records the outcome against the <c>print_job</c> row and warns the
/// cashier; it never rolls anything back.
/// </summary>
public sealed record PrintOutcome
{
    private PrintOutcome(bool succeeded, string? target, string? failureReason)
    {
        Succeeded = succeeded;
        Target = target;
        FailureReason = failureReason;
    }

    /// <summary>True when the document reached the printer.</summary>
    public bool Succeeded { get; }

    /// <summary>Where it went - a printer name, or a file path in development. Null on failure.</summary>
    public string? Target { get; }

    /// <summary>
    /// Plain-language reason it did not print, for the log and the on-screen warning (SRS UI-06).
    /// Null on success.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>The document reached <paramref name="target"/>.</summary>
    public static PrintOutcome Success(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return new PrintOutcome(true, target, null);
    }

    /// <summary>The document did not print, for the stated reason.</summary>
    public static PrintOutcome Failed(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new PrintOutcome(false, null, failureReason);
    }
}

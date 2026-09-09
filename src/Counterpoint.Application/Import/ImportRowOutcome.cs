namespace Counterpoint.Application.Import;

/// <summary>What one spreadsheet row would do, or did, on import (SRS FR-2.22's dry-run preview).</summary>
public enum ImportRowOutcome
{
    /// <summary>No product exists with this row's code; one would be created.</summary>
    Create,

    /// <summary>A product with this row's code already exists; it would be updated.</summary>
    Update,

    /// <summary>The row is entirely blank - nothing to do, and not an error.</summary>
    Skip,

    /// <summary>The row failed validation. Nothing is written for it, and nothing is written for the file it is part of.</summary>
    Error,
}

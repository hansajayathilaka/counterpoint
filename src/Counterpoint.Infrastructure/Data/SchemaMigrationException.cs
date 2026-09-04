using System;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The database could not be brought up to the schema this build expects, or came out of the
/// attempt in a state that cannot be trusted (NFR-M3).
/// </summary>
/// <remarks>
/// Always fatal at start-up. The alternative - carrying on against a half-migrated or corrupt
/// file - is how a day's bills get lost quietly instead of loudly.
/// </remarks>
public sealed class SchemaMigrationException : Exception
{
    public SchemaMigrationException()
    {
    }

    public SchemaMigrationException(string message)
        : base(message)
    {
    }

    public SchemaMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// Supplies the 256-bit SQLCipher key that encrypts the local database at rest (NFR-S3).
/// The key is generated on first run and kept in the operating system's own protected
/// store thereafter (NFR-S6) - never in a configuration file, never in the log.
/// </summary>
/// <remarks>
/// <para>
/// This is a port. Counterpoint.Infrastructure supplies the platform implementations:
/// DPAPI plus Windows Credential Manager on Windows, a file store for Linux development.
/// </para>
/// <para>
/// Deliberately synchronous. It is called exactly once, at start-up, against a local OS
/// store; an async signature would buy nothing and would force blocking waits into the
/// connection-opening path.
/// </para>
/// </remarks>
public interface IDatabaseKeyStore
{
    /// <summary>
    /// Returns the stored database key, generating and persisting one on first run.
    /// </summary>
    public byte[] GetOrCreateKey();
}

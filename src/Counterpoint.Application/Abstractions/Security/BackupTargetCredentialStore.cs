using System;

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// The base every real <see cref="IBackupTargetCredentialStore"/> is built on (SRS FR-11.5,
/// NFR-S6, P4-T01) - the keyed sibling of <see cref="BackupPassphraseStore"/>, built the
/// same way and for the same reason.
/// </summary>
/// <remarks>
/// <b>Internal to <c>Counterpoint.Infrastructure</c>, reached only through the factory.</b> A
/// derived store implements <see cref="Store"/>, <see cref="Remove"/> and <see cref="Read"/>
/// once each; the four public ways in are this class's, which is what keeps "the owner-only write
/// does exactly the same thing to the OS store on every target" a structural fact rather than
/// something that could drift between targets.
/// </remarks>
public abstract class BackupTargetCredentialStore : IBackupTargetCredentialStore
{
    /// <inheritdoc />
    public bool HasCredential(string targetKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetKey);
        return Read(targetKey) is not null;
    }

    /// <inheritdoc />
    public void SetCredential(string targetKey, string credential)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetKey);
        ArgumentException.ThrowIfNullOrEmpty(credential);
        Store(targetKey, credential);
    }

    /// <inheritdoc />
    public void RemoveCredential(string targetKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetKey);
        Remove(targetKey);
    }

    /// <inheritdoc />
    public string? TryGetCredential(string targetKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetKey);
        return Read(targetKey);
    }

    /// <summary>Writes <paramref name="credential"/> to the platform's protected store under <paramref name="targetKey"/>, replacing any previous one.</summary>
    protected abstract void Store(string targetKey, string credential);

    /// <summary>Removes whatever is stored under <paramref name="targetKey"/>. A no-op when there is none.</summary>
    protected abstract void Remove(string targetKey);

    /// <summary>Reads whatever is stored under <paramref name="targetKey"/>, or null.</summary>
    protected abstract string? Read(string targetKey);
}

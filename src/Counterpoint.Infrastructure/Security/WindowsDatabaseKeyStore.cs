using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// The shipping key store: a 256-bit key protected with DPAPI under the current user and kept
/// in Windows Credential Manager (NFR-S3, NFR-S6). Never written to a configuration file and
/// never logged.
/// </summary>
/// <remarks>
/// Two layers on purpose. Credential Manager keeps the blob out of the file system; DPAPI means
/// that a blob lifted out of Credential Manager is useless without the same user's Windows
/// profile. This type cannot be exercised on the Linux development host - it is verified on
/// Windows as part of the P0-T07 installer run.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDatabaseKeyStore : IDatabaseKeyStore
{
    private const string CredentialTargetName = "Counterpoint:DatabaseKey";
    private const string CredentialUserName = "Counterpoint";

    /// <summary>
    /// DPAPI additional entropy. Not a secret - it ties the protected blob to this application
    /// so another program running as the same user cannot unprotect it by accident.
    /// </summary>
    private static readonly byte[] ProtectionEntropy =
        Encoding.UTF8.GetBytes("Counterpoint.DatabaseKey.v1");

    private readonly object _gate = new();
    private readonly string _databaseFilePath;

    public WindowsDatabaseKeyStore(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _databaseFilePath = dataDirectory.DatabaseFilePath;
    }

    public byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            var existing = TryReadExistingKey();
            if (existing is not null)
            {
                return existing;
            }

            // No stored key, but a database. Minting one now would produce a file that can never
            // be opened again, and the shop would find out at the next sale. Refuse instead.
            // Length, not just existence: SQLite creates a zero-byte file when a connection is
            // opened with Mode=ReadWriteCreate, and an empty file holds no bills to orphan.
            if (new FileInfo(_databaseFilePath) is { Exists: true, Length: > 0 })
            {
                throw new InvalidOperationException(
                    $"Counterpoint found its database at \"{_databaseFilePath}\" but no stored key " +
                    "for it. Creating a new key would leave that database permanently unreadable. " +
                    "Sign in as the Windows account that installed Counterpoint, or restore from a " +
                    "backup.");
            }

            var key = RandomNumberGenerator.GetBytes(DatabaseKey.SizeInBytes);
            var protectedKey = ProtectedData.Protect(key, ProtectionEntropy, DataProtectionScope.CurrentUser);
            WindowsCredentialManager.Write(CredentialTargetName, CredentialUserName, protectedKey);
            return key;
        }
    }

    private static byte[]? TryReadExistingKey()
    {
        var protectedKey = WindowsCredentialManager.TryRead(CredentialTargetName);
        if (protectedKey is null)
        {
            return null;
        }

        byte[] key;
        try
        {
            key = ProtectedData.Unprotect(protectedKey, ProtectionEntropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            // Overwriting here would silently orphan the existing database, so refuse instead.
            throw new InvalidOperationException(
                "Counterpoint found a stored database key but this Windows user account cannot " +
                "unlock it. Sign in as the account that installed Counterpoint, or restore from " +
                "a backup.",
                ex);
        }

        if (key.Length != DatabaseKey.SizeInBytes)
        {
            // Same reasoning: a stored key of the wrong size is a damaged credential, not a
            // missing one. Returning null here would mint a replacement over the top of it.
            throw new InvalidOperationException(
                $"The stored Counterpoint database key is {key.Length} bytes; " +
                $"{DatabaseKey.SizeInBytes} are required. The credential is damaged - restore " +
                "from a backup rather than letting Counterpoint create a new key.");
        }

        return key;
    }
}

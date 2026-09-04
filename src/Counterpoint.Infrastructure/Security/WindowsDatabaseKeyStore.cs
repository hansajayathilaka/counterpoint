using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Counterpoint.Application.Abstractions.Security;

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

    public byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            var existing = TryReadExistingKey();
            if (existing is not null)
            {
                return existing;
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

        return key.Length == DatabaseKey.SizeInBytes ? key : null;
    }
}

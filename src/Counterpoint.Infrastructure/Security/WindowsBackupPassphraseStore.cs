using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Counterpoint.Application.Abstractions.Security;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// The shipping backup-passphrase store: the passphrase protected with DPAPI under the current
/// user and kept in Windows Credential Manager (SRS FR-10.7, FR-11.4, NFR-S6).
/// </summary>
/// <remarks>
/// <para>
/// The same two layers as <see cref="WindowsDatabaseKeyStore"/>, and for the same reasons.
/// Credential Manager keeps the passphrase out of the file system; DPAPI means a blob lifted out
/// of Credential Manager is useless without the same Windows profile. Under no circumstances does
/// it go into <c>app_setting</c>: that table lives inside the database the backup is a copy of.
/// </para>
/// <para>
/// This type cannot be exercised on the Linux development host. It is verified on Windows in
/// <c>HW-T05</c>.
/// </para>
/// <para>
/// <b>Internal</b>, for the reason given on <see cref="FileBackupPassphraseStore"/>: replacing the
/// passphrase is owner-only, and the guard is the role-decorated interface the composition root
/// registers, so nothing may hold the concrete store (SRS NFR-S2, AC-17).
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsBackupPassphraseStore : BackupPassphraseStore
{
    private const string CredentialTargetName = "Counterpoint:BackupPassphrase";
    private const string CredentialUserName = "Counterpoint";

    /// <summary>
    /// DPAPI additional entropy. Not a secret - it ties the protected blob to this application so
    /// another program running as the same user cannot unprotect it by accident.
    /// </summary>
    private static readonly byte[] ProtectionEntropy =
        Encoding.UTF8.GetBytes("Counterpoint.BackupPassphrase.v1");

    private readonly object _gate = new();

    /// <inheritdoc />
    public override bool HasPassphrase()
    {
        lock (_gate)
        {
            return WindowsCredentialManager.TryRead(CredentialTargetName) is not null;
        }
    }

    /// <inheritdoc />
    protected override void Store(string passphrase)
    {
        lock (_gate)
        {
            var protectedPassphrase = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(passphrase),
                ProtectionEntropy,
                DataProtectionScope.CurrentUser);

            WindowsCredentialManager.Write(CredentialTargetName, CredentialUserName, protectedPassphrase);
        }
    }

    /// <inheritdoc />
    public override string? TryGetPassphrase()
    {
        lock (_gate)
        {
            var stored = WindowsCredentialManager.TryRead(CredentialTargetName);
            if (stored is null)
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(stored, ProtectionEntropy, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException ex)
            {
                // Refuse rather than report "no passphrase": a caller told there is none would
                // set a new one, and every existing backup would become unrestorable.
                throw new InvalidOperationException(
                    "Counterpoint found a stored backup passphrase but this Windows user account "
                    + "cannot unlock it. Sign in as the account that installed Counterpoint.",
                    ex);
            }
        }
    }
}

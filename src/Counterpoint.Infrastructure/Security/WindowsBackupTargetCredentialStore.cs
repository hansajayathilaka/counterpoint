using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Counterpoint.Application.Abstractions.Security;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// The shipping off-site backup target credential store: each target's credential protected with
/// DPAPI under the current user and kept in Windows Credential Manager (SRS FR-11.5, NFR-S6,
/// P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// The keyed sibling of <see cref="WindowsBackupPassphraseStore"/>, built the same way and for the
/// same reason. One credential per <c>targetKey</c>, under its own Credential Manager entry, so
/// switching the shop's off-site target and switching back does not lose the credential that was
/// there before.
/// </para>
/// <para>This type cannot be exercised on the Linux development host. It is verified on Windows in <c>HW-T05</c>.</para>
/// <para>
/// <b>Internal</b>, for the reason given on <see cref="FileBackupTargetCredentialStore"/>:
/// replacing a credential is owner-only, and the guard is the role-decorated interface the
/// composition root registers, so nothing may hold the concrete store (SRS NFR-S2, AC-17).
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsBackupTargetCredentialStore : BackupTargetCredentialStore
{
    private const string TargetNamePrefix = "Counterpoint:BackupTarget:";
    private const string CredentialUserName = "Counterpoint";

    /// <summary>
    /// DPAPI additional entropy. Not a secret - it ties the protected blob to this application so
    /// another program running as the same user cannot unprotect it by accident.
    /// </summary>
    private static readonly byte[] ProtectionEntropy =
        Encoding.UTF8.GetBytes("Counterpoint.BackupTargetCredential.v1");

    private readonly object _gate = new();

    /// <inheritdoc />
    protected override void Store(string targetKey, string credential)
    {
        lock (_gate)
        {
            var protectedCredential = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(credential),
                ProtectionEntropy,
                DataProtectionScope.CurrentUser);

            WindowsCredentialManager.Write(TargetName(targetKey), CredentialUserName, protectedCredential);
        }
    }

    /// <inheritdoc />
    protected override void Remove(string targetKey)
    {
        lock (_gate)
        {
            WindowsCredentialManager.Delete(TargetName(targetKey));
        }
    }

    /// <inheritdoc />
    protected override string? Read(string targetKey)
    {
        lock (_gate)
        {
            var stored = WindowsCredentialManager.TryRead(TargetName(targetKey));
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
                // Refuse rather than report "no credential": a caller told there is none would
                // offer to set a new one over the top of a target that is actually configured.
                throw new InvalidOperationException(
                    "Counterpoint found a stored backup target credential but this Windows user "
                    + "account cannot unlock it. Sign in as the account that configured it.",
                    ex);
            }
        }
    }

    private static string TargetName(string targetKey) => TargetNamePrefix + targetKey;
}

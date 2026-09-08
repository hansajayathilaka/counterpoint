using System;
using System.Text;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Format;
using Konscious.Security.Cryptography;

namespace Counterpoint.Backup.Crypto;

/// <summary>
/// Turns the owner's backup passphrase into the AES-256 key that encrypts a snapshot
/// (SRS FR-11.4, NFR-S6).
/// </summary>
/// <remarks>
/// Argon2id, not PBKDF2 or a bare hash, and for the same reason <see cref="PasswordHasher"/> uses
/// it: it is memory-hard, so a stolen backup file costs an attacker real memory per guess rather
/// than a cheap hash iteration. Run once a day or once at restore, never on a sale, so its cost is
/// not a latency budget the way sign-in's is.
/// </remarks>
internal static class PassphraseKeyDerivation
{
    /// <summary>Derives a <see cref="BackupFileFormat.KeySizeBytes"/>-byte AES-256 key.</summary>
    public static byte[] DeriveKey(string passphrase, byte[] salt, Argon2Parameters parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(parameters);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            MemorySize = parameters.MemoryKib,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism,
        };

        return argon2.GetBytes(BackupFileFormat.KeySizeBytes);
    }
}

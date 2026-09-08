using System.Text;

namespace Counterpoint.Backup.Format;

/// <summary>Constants shared by <see cref="BackupFileHeader"/>'s writer and reader.</summary>
internal static class BackupFileFormat
{
    /// <summary>Bumped whenever the on-disk layout changes in a way old readers cannot follow.</summary>
    public const byte FormatVersion = 1;

    /// <summary>The first four bytes of every backup file.</summary>
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("CPBK");

    /// <summary>Argon2id salt length. 16 bytes, RFC 9106's recommendation.</summary>
    public const int SaltSizeBytes = 16;

    /// <summary>AES-GCM nonce length. Fixed by the algorithm - <see cref="System.Security.Cryptography.AesGcm.NonceByteSizes"/>.</summary>
    public const int NonceSizeBytes = 12;

    /// <summary>AES-GCM authentication tag length, the maximum the algorithm allows.</summary>
    public const int TagSizeBytes = 16;

    /// <summary>SHA-256 digest length.</summary>
    public const int ChecksumSizeBytes = 32;

    /// <summary>AES-256 key length - the length <see cref="Crypto.PassphraseKeyDerivation"/> derives.</summary>
    public const int KeySizeBytes = 32;
}

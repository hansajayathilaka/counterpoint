using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;

namespace Counterpoint.Backup.Format;

/// <summary>
/// Every field needed to re-derive the AES key and authenticate a backup file, with no dependence
/// on <c>backup_record</c> - a backup exists for exactly the situation where that row, and the
/// whole database, is gone (SRS FR-11.2, FR-11.4).
/// </summary>
/// <remarks>
/// <para>
/// On-disk layout, in order: magic (4 bytes, <c>"CPBK"</c>), format version (1 byte), salt
/// (16 bytes), nonce (12 bytes), Argon2id memory size in KiB / iterations / parallelism (4 bytes
/// each, little-endian), schema version (2-byte length prefix + UTF-8 bytes), taken-at (8 bytes,
/// Unix milliseconds). That block is the AES-GCM associated data - <see cref="BuildAad"/> -
/// tampering with any of it fails authentication on restore, even though none of it is secret.
/// </para>
/// <para>
/// The Argon2id cost parameters travel with the file rather than being assumed from whatever this
/// build currently uses: strengthening them in a later release must not break restoring an older
/// backup, whose key can only be re-derived with the exact parameters it was created under.
/// </para>
/// <para>
/// The AES-GCM tag (16 bytes) and the SHA-256 checksum of the ciphertext (32 bytes) follow the
/// associated-data block, then the ciphertext runs to the end of the file.
/// </para>
/// </remarks>
internal sealed record BackupFileHeader(
    byte[] Salt,
    byte[] Nonce,
    Argon2Parameters Argon2,
    string SchemaVersion,
    DateTimeOffset TakenAt)
{
    /// <summary>Serialises the associated-data block described in the class remarks.</summary>
    public byte[] BuildAad()
    {
        using var buffer = new MemoryStream();
        buffer.Write(BackupFileFormat.Magic);
        buffer.WriteByte(BackupFileFormat.FormatVersion);
        buffer.Write(Salt);
        buffer.Write(Nonce);
        WriteInt32(buffer, Argon2.MemoryKib);
        WriteInt32(buffer, Argon2.Iterations);
        WriteInt32(buffer, Argon2.Parallelism);

        var schemaBytes = Encoding.UTF8.GetBytes(SchemaVersion);
        if (schemaBytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("The schema version string is implausibly long.");
        }

        WriteUInt16(buffer, (ushort)schemaBytes.Length);
        buffer.Write(schemaBytes);
        WriteInt64(buffer, TakenAt.ToUnixTimeMilliseconds());

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes the associated-data block, the tag and the checksum - everything before the
    /// ciphertext - to <paramref name="destination"/>.
    /// </summary>
    public async Task WriteHeaderAsync(
        Stream destination,
        byte[] tag,
        byte[] checksum,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(checksum);

        await destination.WriteAsync(BuildAad(), cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(checksum, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the header, the tag and the checksum from the start of <paramref name="source"/>,
    /// leaving the stream positioned at the first byte of ciphertext.
    /// </summary>
    /// <exception cref="Counterpoint.Backup.BackupRestoreException">
    /// The file is too short, does not start with the magic bytes, or was written in a format
    /// version this build does not understand.
    /// </exception>
    public static async Task<(BackupFileHeader Header, byte[] Tag, byte[] Checksum, byte[] Aad)> ReadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var magic = await ReadExactAsync(source, BackupFileFormat.Magic.Length, cancellationToken)
            .ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(BackupFileFormat.Magic))
        {
            throw new Counterpoint.Backup.BackupRestoreException(
                "This file is not a Counterpoint backup: its header does not match.");
        }

        var formatVersion = (await ReadExactAsync(source, 1, cancellationToken).ConfigureAwait(false))[0];
        if (formatVersion != BackupFileFormat.FormatVersion)
        {
            throw new Counterpoint.Backup.BackupRestoreException(
                $"This backup was written in format {formatVersion}, which this version of " +
                "Counterpoint does not understand.");
        }

        var salt = await ReadExactAsync(source, BackupFileFormat.SaltSizeBytes, cancellationToken)
            .ConfigureAwait(false);
        var nonce = await ReadExactAsync(source, BackupFileFormat.NonceSizeBytes, cancellationToken)
            .ConfigureAwait(false);
        var memoryKib = ReadInt32(await ReadExactAsync(source, 4, cancellationToken).ConfigureAwait(false));
        var iterations = ReadInt32(await ReadExactAsync(source, 4, cancellationToken).ConfigureAwait(false));
        var parallelism = ReadInt32(await ReadExactAsync(source, 4, cancellationToken).ConfigureAwait(false));
        var schemaLength = ReadUInt16(await ReadExactAsync(source, 2, cancellationToken).ConfigureAwait(false));
        var schemaBytes = await ReadExactAsync(source, schemaLength, cancellationToken).ConfigureAwait(false);
        var takenAtMs = ReadInt64(await ReadExactAsync(source, 8, cancellationToken).ConfigureAwait(false));
        var tag = await ReadExactAsync(source, BackupFileFormat.TagSizeBytes, cancellationToken)
            .ConfigureAwait(false);
        var checksum = await ReadExactAsync(source, BackupFileFormat.ChecksumSizeBytes, cancellationToken)
            .ConfigureAwait(false);

        var header = new BackupFileHeader(
            salt,
            nonce,
            new Argon2Parameters(memoryKib, iterations, parallelism),
            Encoding.UTF8.GetString(schemaBytes),
            DateTimeOffset.FromUnixTimeMilliseconds(takenAtMs));

        // Rebuilt from the parsed fields rather than captured as raw bytes while reading: it is a
        // pure function of the header, so the two are guaranteed to agree, and there is no second
        // code path to keep in step with BuildAad.
        return (header, tag, checksum, header.BuildAad());
    }

    private static async Task<byte[]> ReadExactAsync(Stream source, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await source.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0)
            {
                throw new Counterpoint.Backup.BackupRestoreException(
                    "This backup file is shorter than a valid Counterpoint backup header - it is " +
                    "truncated or is not a backup at all.");
            }

            read += n;
        }

        return buffer;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static int ReadInt32(byte[] bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes);

    private static ushort ReadUInt16(byte[] bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    private static long ReadInt64(byte[] bytes) => BinaryPrimitives.ReadInt64LittleEndian(bytes);
}

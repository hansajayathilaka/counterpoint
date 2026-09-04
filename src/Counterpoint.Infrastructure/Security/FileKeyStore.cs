using System;
using System.IO;
using System.Security.Cryptography;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// DEVELOPMENT ONLY - NEVER SHIPPED.
/// </summary>
/// <remarks>
/// <para>
/// Keeps the database key in a file beside the database so the stack can be built and tested
/// on Linux, where DPAPI and Windows Credential Manager do not exist (CLAUDE.md "Development
/// platform note"). A key sitting next to the file it unlocks protects nothing; on Windows the
/// key lives in Credential Manager under DPAPI instead - see <see cref="WindowsDatabaseKeyStore"/>.
/// </para>
/// <para>
/// The installer must never lay this down, and <see cref="DatabaseKeyStoreFactory"/> only
/// selects it off Windows.
/// </para>
/// </remarks>
public sealed class FileKeyStore : IDatabaseKeyStore
{
    /// <summary>Name of the development key file inside the data directory.</summary>
    public const string KeyFileName = "database.devkey";

    private readonly object _gate = new();
    private readonly string _keyFilePath;

    public FileKeyStore(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _keyFilePath = Path.Combine(dataDirectory.Root, KeyFileName);
    }

    /// <summary>Full path of the key file. Exposed so a test can assert its permissions.</summary>
    public string KeyFilePath => _keyFilePath;

    public byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            if (File.Exists(_keyFilePath))
            {
                return ReadExistingKey();
            }

            var key = RandomNumberGenerator.GetBytes(DatabaseKey.SizeInBytes);
            var directory = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_keyFilePath, Convert.ToHexString(key));
            RestrictToOwner(_keyFilePath);
            return key;
        }
    }

    private byte[] ReadExistingKey()
    {
        byte[] key;
        try
        {
            key = Convert.FromHexString(File.ReadAllText(_keyFilePath).Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"The development database key file \"{_keyFilePath}\" is not readable hexadecimal. " +
                "Delete it only if you are willing to lose the development database it unlocks.",
                ex);
        }

        if (key.Length != DatabaseKey.SizeInBytes)
        {
            throw new InvalidOperationException(
                $"The development database key file \"{_keyFilePath}\" holds {key.Length} bytes; " +
                $"{DatabaseKey.SizeInBytes} are required.");
        }

        return key;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows never uses this store; the installer's ACLs on %ProgramData%\Counterpoint
            // are the control there.
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

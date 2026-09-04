using System;
using System.IO;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Security;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// A real, encrypted SQLite database in a throwaway folder, deleted when the test finishes.
/// No in-memory provider: it enforces neither foreign keys nor triggers, which are precisely
/// what the infrastructure tests exist to check (engineering guide §6).
/// </summary>
public sealed class TemporaryDataDirectory : IDisposable
{
    private readonly string _root;

    public TemporaryDataDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "counterpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        DataDirectory = PosDataDirectory.Resolve(_root).EnsureCreated();
        KeyStore = new FileKeyStore(DataDirectory);
    }

    /// <summary>The validated data directory under the temp folder.</summary>
    public PosDataDirectory DataDirectory { get; }

    /// <summary>The key store backing every factory this fixture creates.</summary>
    public IDatabaseKeyStore KeyStore { get; }

    /// <summary>Opens a factory over this directory. Several may be created in turn to prove reopening works.</summary>
    public PosConnectionFactory CreateConnectionFactory() => new(DataDirectory, KeyStore);

    /// <summary>Opens a factory over this directory with a different key, to prove the wrong key fails.</summary>
    public PosConnectionFactory CreateConnectionFactory(IDatabaseKeyStore keyStore) =>
        new(DataDirectory, keyStore);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
            // A stray temp folder is not worth failing a green test run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

using System;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// Picks the key store for the host platform: the OS credential store on Windows, the
/// development file store anywhere else.
/// </summary>
public static class DatabaseKeyStoreFactory
{
    public static IDatabaseKeyStore Create(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDatabaseKeyStore();
        }

        // Linux and macOS are development hosts only (CLAUDE.md "Development platform note").
        return new FileKeyStore(dataDirectory);
    }
}

using Counterpoint.Application.Abstractions.Security;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>A key store that always returns the key it was handed. Used to open a database with the wrong key.</summary>
internal sealed class FixedKeyStore : IDatabaseKeyStore
{
    private readonly byte[] _key;

    public FixedKeyStore(byte[] key) => _key = key;

    public byte[] GetOrCreateKey() => _key;
}

using System;
using System.IO;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Security;
using Counterpoint.Integration.Tests.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The development key store (NFR-S3, NFR-S6). It never ships, but it is the only thing
/// standing between a shared development box and the database it unlocks, and the reopen
/// path in <c>PosConnectionFactoryTests</c> depends on it returning a stable key.
/// </summary>
public sealed class FileKeyStoreTests
{
    /// <summary>Owner read/write and nothing else: 0600.</summary>
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void NFR_S6_KeyFileIsCreatedReadableOnlyByItsOwner()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileKeyStore(fixture.DataDirectory);

        File.Exists(store.KeyFilePath).Should().BeFalse("the fixture must start without a key file");

        store.GetOrCreateKey();

        File.Exists(store.KeyFilePath).Should().BeTrue();

        if (OperatingSystem.IsWindows())
        {
            // There are no Unix permissions to assert, and this store is never selected on
            // Windows: the key lives in Credential Manager under DPAPI instead. Assert that,
            // because it is the reason the permission question does not arise there.
            DatabaseKeyStoreFactory.Create(fixture.DataDirectory)
                .Should().NotBeOfType<FileKeyStore>(
                    "the development file store must never be selected on the shipping platform");
            return;
        }

        File.GetUnixFileMode(store.KeyFilePath).Should().Be(
            OwnerOnly,
            "the key that decrypts the till's database must not be readable by group or world");
    }

    [Fact]
    public void KeyIsTwoHundredAndFiftySixBitsAndStableAcrossInstances()
    {
        using var fixture = new TemporaryDataDirectory();

        var created = new FileKeyStore(fixture.DataDirectory).GetOrCreateKey();

        created.Should().HaveCount(DatabaseKey.SizeInBytes, "SQLCipher is keyed with 256 bits");
        created.Should().Contain(b => b != 0, "a key of zeroes would mean the RNG never ran");

        // A new instance stands in for the next run of the app: same key, or yesterday's
        // database is gone.
        var reread = new FileKeyStore(fixture.DataDirectory).GetOrCreateKey();

        reread.Should().Equal(created);
    }

    [Fact]
    public void EachDataDirectoryGetsItsOwnRandomKey()
    {
        using var first = new TemporaryDataDirectory();
        using var second = new TemporaryDataDirectory();

        var one = new FileKeyStore(first.DataDirectory).GetOrCreateKey();
        var two = new FileKeyStore(second.DataDirectory).GetOrCreateKey();

        one.Should().NotEqual(two, "the key must be generated, not compiled in");
    }

    [Fact]
    public void RefusesAKeyFileThatIsNotHexadecimal()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileKeyStore(fixture.DataDirectory);
        File.WriteAllText(store.KeyFilePath, "this is not a key");

        var read = () => store.GetOrCreateKey();

        read.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(store.KeyFilePath)
            .And.Contain(
                "hexadecimal",
                "silently generating a fresh key here would abandon the existing database");
    }

    [Fact]
    public void RefusesAKeyFileOfTheWrongLength()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileKeyStore(fixture.DataDirectory);
        File.WriteAllText(store.KeyFilePath, Convert.ToHexString(new byte[16]));

        var read = () => store.GetOrCreateKey();

        read.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("16 bytes");
    }

    [Fact]
    public void TheFactoryPicksTheFileStoreOnDevelopmentHostsOnly()
    {
        using var fixture = new TemporaryDataDirectory();

        IDatabaseKeyStore store = DatabaseKeyStoreFactory.Create(fixture.DataDirectory);

        if (OperatingSystem.IsWindows())
        {
            store.Should().BeOfType<WindowsDatabaseKeyStore>();
        }
        else
        {
            store.Should().BeOfType<FileKeyStore>();
        }
    }
}

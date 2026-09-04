using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// The data directory guard (engineering guide §4.9). A refusal must be a plain-language
/// exception naming the folder, never a crash somewhere deep in SQLite.
/// </summary>
public sealed class PosDataDirectoryTests
{
    [Theory]
    [InlineData(@"C:\Users\shop\OneDrive\Counterpoint")]
    [InlineData(@"C:\Users\shop\OneDrive - Hardware Stores Ltd\Counterpoint")]
    [InlineData(@"C:\Users\shop\Google Drive\Counterpoint")]
    [InlineData(@"C:\Users\shop\Dropbox\Counterpoint")]
    [InlineData("/home/shop/Dropbox/counterpoint")]
    public void RefusesACloudSyncFolderWithAPlainLanguageMessage(string path)
    {
        var resolve = () => PosDataDirectory.Resolve(path);

        resolve.Should().Throw<InvalidDataDirectoryException>()
            .Which.Message.Should().Contain(path)
            .And.Contain("ProgramData", "the message has to tell the owner where to put it instead");
    }

    [Fact]
    public void RefusesAUncPath()
    {
        var resolve = () => PosDataDirectory.Resolve(@"\\backupserver\till\Counterpoint");

        resolve.Should().Throw<InvalidDataDirectoryException>()
            .Which.Message.Should().Contain("another computer");
    }

    [Fact]
    public void AcceptsAFolderThatMerelyMentionsASyncProduct()
    {
        using var fixture = new TemporaryDataDirectory();

        var sibling = System.IO.Path.Combine(fixture.DataDirectory.Root, "Dropbox Receipts");

        var resolved = PosDataDirectory.Resolve(sibling);

        resolved.Root.Should().Be(sibling, "the match is segment-wise, not a substring search");
    }

    [Fact]
    public void EnsureCreatedMakesTheDatabaseFolder()
    {
        using var fixture = new TemporaryDataDirectory();

        System.IO.Directory.Exists(fixture.DataDirectory.DatabaseDirectory).Should().BeTrue();
        fixture.DataDirectory.DatabaseFilePath.Should().EndWith(PosDataDirectory.DatabaseFileName);
    }
}

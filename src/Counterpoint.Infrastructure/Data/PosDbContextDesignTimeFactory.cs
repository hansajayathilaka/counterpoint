using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Builds a <see cref="PosDbContext"/> for the <c>dotnet ef</c> tools.
/// </summary>
/// <remarks>
/// <para>
/// Used only when a developer regenerates a migration, and only under <c>EfTooling=true</c>
/// (docs/adr/0004). It is never registered in dependency injection and never reachable at
/// runtime: the shipped application only ever gets a context through
/// <see cref="IPosDbContextFactory"/>, on the write connection's open transaction.
/// </para>
/// <para>
/// The connection string points at a throwaway unencrypted temp file with no key and no PRAGMAs,
/// because the tools never open it - they only need a provider so the SQLite migration generator
/// can be resolved. Nothing real is ever written there.
/// </para>
/// </remarks>
internal sealed class PosDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(Path.GetTempPath(), "counterpoint-design-time.db"))
            .Options);
}

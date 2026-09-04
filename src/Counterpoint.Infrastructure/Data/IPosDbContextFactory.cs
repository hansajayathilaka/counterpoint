namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The only way to obtain a <see cref="PosDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core is the write and migration path, and every write goes through the one connection
/// held behind the write gate (engineering guide §4.8). A <c>DbContext</c> registered the usual
/// way - <c>AddDbContext</c> with its own connection - would open a second connection to the
/// same file per scope and write outside both the gate and the unit of work's transaction, so
/// that registration deliberately does not exist.
/// </para>
/// <para>
/// The implementation therefore only hands out a context bound to the write connection and the
/// transaction currently open on this async flow, and throws when there is none.
/// <see cref="PosDbContext"/>'s constructor is internal so no other assembly can go around it.
/// </para>
/// </remarks>
public interface IPosDbContextFactory
{
    /// <summary>
    /// Creates a context on the open unit of work's connection and transaction. The caller
    /// disposes it; disposing it does not close the connection or end the transaction.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// No unit of work is open on this async flow.
    /// </exception>
    public PosDbContext CreateDbContext();
}

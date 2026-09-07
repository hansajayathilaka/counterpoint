using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Security;

/// <summary>
/// Gives the shop's owner account its first password, on a database that has none
/// (SRS FR-1.1, FR-1.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> There is no default password anywhere in this system - the
/// account written on first run carries a placeholder that nothing can authenticate against
/// (docs/01_DATA_MODEL.md §11). Requiring a login before any operation therefore leaves a brand
/// new database with no way in, and one has to be opened exactly once.
/// </para>
/// <para>
/// <b>Why it is safe.</b> <see cref="CompleteAsync"/> works only while
/// <see cref="IsRequiredAsync"/> is true - that is, while no account in the database has a hash
/// any password could verify against. The moment the first credential exists this becomes a
/// closed door for good, and resetting a password afterwards is the owner's job through
/// <see cref="IUserAdministration"/>.
/// </para>
/// <para>
/// The full first-run wizard - shop profile, tax, numbering, printer, backup passphrase - is
/// P1-T03. This is the one step of it that authentication cannot wait for.
/// </para>
/// </remarks>
public interface IInitialOwnerSetup
{
    /// <summary>True when no account in the database has a usable password yet.</summary>
    public Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the first password on an active owner account.
    /// </summary>
    /// <exception cref="NotAuthorisedException">
    /// The shop already has a usable credential. Changing a password from here would be a way
    /// round <see cref="IUserAdministration"/> and its owner-only check.
    /// </exception>
    public Task CompleteAsync(string username, string password, CancellationToken cancellationToken = default);
}

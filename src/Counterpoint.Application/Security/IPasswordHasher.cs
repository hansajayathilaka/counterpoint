namespace Counterpoint.Application.Security;

/// <summary>
/// Turns a password or PIN into something safe to store, and checks one against what was stored
/// (SRS FR-1.3, NFR-S1).
/// </summary>
/// <remarks>
/// There is deliberately no "decrypt", "reveal" or "get password" on this interface, and there
/// never will be. A hash is one-way; a forgotten password is reset by the owner, not recovered.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="password"/> with a fresh random salt and returns the encoded
    /// string to store in <c>app_user.password_hash</c>.
    /// </summary>
    public string Hash(string password);

    /// <summary>
    /// True when <paramref name="password"/> is the one <paramref name="encodedHash"/> was made
    /// from. False for anything else, including a malformed or placeholder hash - it never throws
    /// on bad stored data, because a login screen must not be able to tell those apart.
    /// </summary>
    public bool Verify(string password, string? encodedHash);

    /// <summary>
    /// True when <paramref name="encodedHash"/> is a hash some password could verify against.
    /// </summary>
    /// <remarks>
    /// The account seeded on first run carries a placeholder that is not an Argon2id string, so
    /// nothing can authenticate as it (docs/01_DATA_MODEL.md §11). This is how the first-run
    /// owner-password step knows the shop has no usable credential yet.
    /// </remarks>
    public bool IsUsable(string? encodedHash);
}

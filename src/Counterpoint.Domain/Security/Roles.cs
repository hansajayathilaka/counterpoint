using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Counterpoint.Domain.Security;

/// <summary>
/// The rules about <see cref="Role"/>: what it is called in the database, and what it lets
/// through (SRS §3.3, FR-1.2).
/// </summary>
/// <remarks>
/// The two tokens are the ones <c>app_user.role</c> is constrained to by
/// <c>CHECK (role IN ('CASHIER','OWNER'))</c> (docs/01_DATA_MODEL.md §8). They are spelled out
/// here, once, so no adapter has to spell them again and no adapter can spell them differently.
/// </remarks>
public static class Roles
{
    /// <summary>The <c>app_user.role</c> value for <see cref="Role.Cashier"/>.</summary>
    public const string CashierToken = "CASHIER";

    /// <summary>The <c>app_user.role</c> value for <see cref="Role.Owner"/>.</summary>
    public const string OwnerToken = "OWNER";

    /// <summary>The database token for a role.</summary>
    public static string ToToken(Role role) => role switch
    {
        Role.Cashier => CashierToken,
        Role.Owner => OwnerToken,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "There are exactly two roles."),
    };

    /// <summary>Reads a database token back into a role.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is not one of the two the schema allows. Refusing is the only safe answer: a
    /// role nobody recognises must never be treated as the higher privilege, and treating it as
    /// the lower one silently would hide a corrupt row.
    /// </exception>
    public static Role Parse(string token)
    {
        if (TryParse(token, out var role))
        {
            return role;
        }

        throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{token}' is not a role. app_user.role is constrained to '{CashierToken}' or '{OwnerToken}'."));
    }

    /// <summary>Reads a database token back into a role, without throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? token, out Role role)
    {
        switch (token)
        {
            case CashierToken:
                role = Role.Cashier;
                return true;
            case OwnerToken:
                role = Role.Owner;
                return true;
            default:
                role = Role.Cashier;
                return false;
        }
    }

    /// <summary>
    /// True when a user holding <paramref name="held"/> may perform something that requires
    /// <paramref name="required"/>.
    /// </summary>
    /// <remarks>
    /// An owner can do everything a cashier can do (SRS §3.3), so the answer is not equality.
    /// It is written as an explicit switch rather than <c>held &gt;= required</c> so that the
    /// permission split does not silently depend on the order the enum members happen to be
    /// declared in.
    /// </remarks>
    public static bool Satisfies(Role held, Role required) => (held, required) switch
    {
        (Role.Owner, _) => true,
        (Role.Cashier, Role.Cashier) => true,
        _ => false,
    };
}

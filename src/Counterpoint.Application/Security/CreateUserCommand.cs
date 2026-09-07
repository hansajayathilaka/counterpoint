using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>A new account for the owner to create (SRS FR-1.4).</summary>
/// <param name="Username">What they will type to sign in. Unique.</param>
/// <param name="DisplayName">What the screen will call them.</param>
/// <param name="Password">
/// The password or PIN, in the clear, for exactly as long as it takes
/// <see cref="PasswordHasher"/> to hash it. It is never stored, never logged and never audited.
/// </param>
/// <param name="Role">Cashier or owner (SRS §3.3).</param>
public sealed record CreateUserCommand(string Username, string DisplayName, string Password, Role Role);

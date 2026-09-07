using System;

namespace Counterpoint.Application.Security;

/// <summary>
/// The signed-in user may not do that (SRS NFR-S2, AC-17).
/// </summary>
/// <remarks>
/// <para>
/// Thrown by <see cref="RoleAuthorisation"/> <em>before</em> the service it guards is called, so
/// nothing has happened when it surfaces: no row read, no row written, no partial result. That
/// is the whole point of the type - a refusal is not a value a caller can accidentally use.
/// </para>
/// <para>
/// It derives from <see cref="Exception"/> and not from <see cref="InvalidOperationException"/>
/// on purpose. The viewmodels catch <see cref="InvalidOperationException"/> to turn a business
/// rule into a sentence on the screen; an authorisation refusal must be caught deliberately,
/// where the screen can decide whether to offer an owner override, rather than being swallowed
/// into a status line by a handler that was written for something else.
/// </para>
/// </remarks>
public sealed class NotAuthorisedException : Exception
{
    public NotAuthorisedException()
        : base("You do not have permission to do that.")
    {
    }

    public NotAuthorisedException(string message)
        : base(message)
    {
    }

    public NotAuthorisedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

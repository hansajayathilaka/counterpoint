namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Who is at the till and which shift they are trading in.
/// </summary>
/// <param name="UserId">The signed-in user.</param>
/// <param name="ShiftId">The one open shift (C-01: the database permits no second).</param>
public sealed record TillSession(long UserId, long ShiftId);

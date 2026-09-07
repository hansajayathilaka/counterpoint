namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The password and lockout settings in force, as they are written to <c>app_setting</c>.
/// </summary>
/// <param name="Argon2MemoryKib">Argon2id memory cost, in kibibytes.</param>
/// <param name="Argon2Iterations">Argon2id time cost.</param>
/// <param name="Argon2Parallelism">Argon2id lanes.</param>
/// <param name="MinimumPasswordLength">Shortest password or PIN the shop may set.</param>
/// <param name="MaxFailedAttempts">Consecutive failures before the account locks (SRS NFR-S9).</param>
/// <param name="LockoutBaseSeconds">The lock applied at that failure.</param>
/// <param name="LockoutMaximumSeconds">The ceiling the exponential backoff is held at.</param>
/// <remarks>
/// Every value is a plain integer, which is why they are all <c>INT</c> rows rather than one
/// <c>JSON</c> blob: the owner can read them, and <c>HW-T07</c> can compare what the terminal was
/// tuned to against what is stored.
/// </remarks>
public sealed record RecordedSecurityPolicy(
    int Argon2MemoryKib,
    int Argon2Iterations,
    int Argon2Parallelism,
    int MinimumPasswordLength,
    int MaxFailedAttempts,
    int LockoutBaseSeconds,
    int LockoutMaximumSeconds);

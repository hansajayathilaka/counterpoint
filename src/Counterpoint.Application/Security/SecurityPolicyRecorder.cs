using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Application.Security;

/// <summary>
/// Writes the Argon2id work factors and the lockout policy in force into <c>app_setting</c>,
/// on every start.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written, not read.</b> Today the code is the source of truth for these numbers, so the rows
/// are a record and not a configuration: the owner can see what their hashes were made with, and
/// <c>HW-T07</c> can compare the terminal's tuning against what the database says. Writing them
/// every start rather than only when absent is deliberate - a row left behind by an older build
/// would be a confident, checkable lie.
/// </para>
/// <para>
/// P1-T03 turns this round. Once <c>ISettings</c> and <c>SettingDefaults</c> exist the rows
/// become the source and these values their defaults, and this class goes away into that.
/// </para>
/// </remarks>
public sealed class SecurityPolicyRecorder
{
    /// <summary>The <c>app_setting.key</c> for the Argon2id memory cost, in kibibytes.</summary>
    public const string Argon2MemoryKibKey = "security.password.argon2.memory_kib";

    /// <summary>The <c>app_setting.key</c> for the Argon2id time cost.</summary>
    public const string Argon2IterationsKey = "security.password.argon2.iterations";

    /// <summary>The <c>app_setting.key</c> for the Argon2id lane count.</summary>
    public const string Argon2ParallelismKey = "security.password.argon2.parallelism";

    /// <summary>The <c>app_setting.key</c> for the shortest allowed password or PIN.</summary>
    public const string MinimumPasswordLengthKey = "security.password.minimum_length";

    /// <summary>The <c>app_setting.key</c> for the failure count that locks an account.</summary>
    public const string MaxFailedAttemptsKey = "security.login.max_failed_attempts";

    /// <summary>The <c>app_setting.key</c> for the first lockout's length, in seconds.</summary>
    public const string LockoutBaseSecondsKey = "security.login.lockout_base_seconds";

    /// <summary>The <c>app_setting.key</c> for the lockout ceiling, in seconds.</summary>
    public const string LockoutMaximumSecondsKey = "security.login.lockout_max_seconds";

    private readonly ISecurityPolicyStore _store;
    private readonly Argon2Parameters _parameters;

    public SecurityPolicyRecorder(ISecurityPolicyStore store, Argon2Parameters parameters)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(parameters);

        _store = store;
        _parameters = parameters;
    }

    /// <summary>The policy this build enforces, as it is written to <c>app_setting</c>.</summary>
    public RecordedSecurityPolicy Current => new(
        _parameters.MemoryKib,
        _parameters.Iterations,
        _parameters.Parallelism,
        PasswordHasher.MinimumPasswordLength,
        AccountLockout.MaxFailedAttempts,
        Seconds(AccountLockout.BaseDuration),
        Seconds(AccountLockout.MaximumDuration));

    /// <summary>Records the policy. Safe on every start.</summary>
    public Task EnsureRecordedAsync(CancellationToken cancellationToken = default) =>
        _store.RecordAsync(Current, cancellationToken);

    /// <summary>
    /// Whole seconds, as an integer. Both durations are whole minutes or less by construction, so
    /// there is nothing to lose - and <c>TimeSpan.TotalSeconds</c> is a <c>double</c>, which this
    /// layer does not use (CLAUDE.md invariant 1).
    /// </summary>
    private static int Seconds(TimeSpan duration) =>
        (int)(duration.Ticks / TimeSpan.TicksPerSecond);
}

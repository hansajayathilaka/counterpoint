namespace Counterpoint.Application.Security;

/// <summary>
/// The Argon2id work factors <see cref="PasswordHasher"/> hashes with (SRS FR-1.3, NFR-S1).
/// </summary>
/// <param name="MemoryKib">Memory cost in kibibytes. 65 536 KiB is 64 MB.</param>
/// <param name="Iterations">Time cost: the number of passes over that memory.</param>
/// <param name="Parallelism">Lanes the work is split across.</param>
/// <remarks>
/// <para>
/// <b>Why these numbers.</b> 64 MB, three passes, four lanes is RFC 9106's second recommended
/// option verbatim. The memory cost is what a stolen database file costs an attacker - 64 MB per
/// guess - and it is the single most effective dial, so it is the one held fixed.
/// </para>
/// <para>
/// <b>Why four lanes and not one.</b> P1-T02 proposed <c>p=1</c>, and measurement said no: the
/// managed Argon2 implementation takes a median of 450 ms at <c>p=1</c> on the development
/// machine, which puts a whole sign-in past the 500 ms budget the same task sets. Lanes do not
/// change the total work, only how it is divided, so <c>p=4</c> costs an attacker exactly the
/// same 64 MB and three passes while a machine with cores to spare finishes in a median of
/// 180 ms. On a single-core terminal the four lanes simply run one after another and it costs
/// what <c>p=1</c> costs - so this is never the slower choice.
/// </para>
/// <para>
/// Both figures are development-machine numbers and are worth nothing as budgets. The real
/// measurement, and the retune to "login under 200 ms" on the minimum-spec terminal, is
/// <c>HW-T07</c>.
/// </para>
/// <para>
/// The values in force are written to <c>app_setting</c> on every start by
/// <see cref="SecurityPolicyRecorder"/>, so what the shop's database was hashed with is a matter
/// of record rather than of reading this file at the right commit. A hash is self-describing
/// (see <see cref="PasswordHasher"/>), so retuning these does not invalidate an account.
/// </para>
/// </remarks>
public sealed record Argon2Parameters(int MemoryKib, int Iterations, int Parallelism)
{
    /// <summary>Salt length in bytes. 16 is the RFC 9106 recommendation.</summary>
    public const int SaltBytes = 16;

    /// <summary>Derived key length in bytes.</summary>
    public const int HashBytes = 32;

    /// <summary>
    /// The parameters this build hashes with: 64 MB, three passes, four lanes.
    /// </summary>
    public static Argon2Parameters Default { get; } = new(MemoryKib: 65_536, Iterations: 3, Parallelism: 4);
}

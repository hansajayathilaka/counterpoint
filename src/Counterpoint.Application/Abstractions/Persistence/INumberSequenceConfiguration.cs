using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Applies the FR-10.4 numbering settings to the <c>number_sequence</c> rows the allocator draws
/// from.
/// </summary>
/// <remarks>
/// <para>
/// Two callers only: the first-run wizard, which creates the series, and
/// <c>ISettings.SaveAsync</c>, which keeps a series' prefix and pattern in step with the setting
/// the owner just edited. Both are inside a transaction; this joins it.
/// </para>
/// <para>
/// <b><see cref="ConfigureAsync"/> never moves the counter.</b> <c>next_val</c> is set once, when
/// the row is created, and never afterwards by an edit. A number that has been issued is never
/// reissued and a cancelled document keeps its number, which is what makes the series gapless
/// (CLAUDE.md invariant 4, AC-19).
/// </para>
/// <para>
/// <b><see cref="InitialiseAsync"/> is the one exception, and it belongs to first run alone.</b>
/// <c>FirstRunSeeder</c> creates the <c>SALE</c> and <c>SHIFT</c> rows at 1 before the wizard is
/// ever shown, so a wizard that could only ever create a row would silently drop the starting
/// number the owner chose for exactly those two series - the <c>app_setting</c> row would say
/// 5000 and the counter would say 1, for ever, with nothing to reconcile them. Setting the
/// counter there is not "editing a live counter": first run is guarded by
/// <c>IFirstRunSetup.IsRequiredAsync</c>, runs once, and runs on a database in which no document
/// of any kind has been issued. Implementations must refuse it on a database that has completed
/// its first run.
/// </para>
/// </remarks>
public interface INumberSequenceConfiguration
{
    /// <summary>
    /// Creates the series for <paramref name="documentType"/> if it is absent, starting at
    /// <paramref name="startingNumber"/>; otherwise updates only its prefix and pattern - never
    /// the counter.
    /// </summary>
    /// <returns>True when a row was created or changed.</returns>
    public Task<bool> ConfigureAsync(
        string documentType,
        string prefix,
        string pattern,
        long startingNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// First run only: creates the series for <paramref name="documentType"/> or completes the
    /// one the seeder laid down, counter included, so that the first document the shop issues
    /// carries the number the owner asked for (FR-10.4).
    /// </summary>
    /// <returns>True when a row was created or changed.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// The database has already completed first run. After that the counter is the allocator's
    /// alone and only <see cref="ConfigureAsync"/> may touch the series.
    /// </exception>
    public Task<bool> InitialiseAsync(
        string documentType,
        string prefix,
        string pattern,
        long startingNumber,
        CancellationToken cancellationToken = default);
}

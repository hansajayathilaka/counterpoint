using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Settings.FirstRun;

/// <summary>
/// Turns an empty database into a shop that can trade (SRS FR-10, FR-1.3).
/// </summary>
/// <remarks>
/// <para>
/// Headless on purpose. The wizard's windows are a separate piece of work; everything they do to
/// the database happens here, so the setup can be run, tested and re-run without a screen, and so
/// that no rule lives in a viewmodel (CLAUDE.md invariant 8).
/// </para>
/// <para>
/// One transaction, and idempotent: settings, tax classes, number series and the owner's first
/// password all commit together or not at all, and running it against a database that has
/// already been set up changes nothing and says so.
/// </para>
/// </remarks>
public interface IFirstRunSetup
{
    /// <summary>True when this database has never been through the wizard.</summary>
    public Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the setup.
    /// </summary>
    /// <returns>
    /// True when it configured the shop; false when the database had already been set up and
    /// nothing was changed.
    /// </returns>
    public Task<bool> CompleteAsync(
        FirstRunSetupRequest request,
        CancellationToken cancellationToken = default);
}

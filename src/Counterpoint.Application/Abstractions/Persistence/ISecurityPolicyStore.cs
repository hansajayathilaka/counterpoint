using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes the password and lockout settings in force into <c>app_setting</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately write-only, and deliberately narrow. Today the code is the source of truth for
/// these numbers and the rows are a record of what the shop's hashes were made with -
/// P1-T02's own choices, visible without reading this repository at the right commit.
/// </para>
/// <para>
/// P1-T03 builds the general settings framework (<c>ISettings</c>, <c>SettingDefaults</c>) and
/// inverts that direction: the rows become the source and these values its defaults. This port
/// is the seam where that inversion happens, which is why it names its seven fields rather than
/// taking an untyped bag of keys - a general settings writer here would be P1-T03's job done
/// early and badly.
/// </para>
/// </remarks>
public interface ISecurityPolicyStore
{
    /// <summary>Writes each setting, replacing any value already there.</summary>
    public Task RecordAsync(RecordedSecurityPolicy policy, CancellationToken cancellationToken = default);
}

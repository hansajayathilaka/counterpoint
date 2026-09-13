using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Resolves <see cref="PolicySettings.NonReturnableCategoryIds"/> to display names and hands the
/// result to <see cref="ReturnPolicyTextFormatter"/> - the one policy-text sentence printed on
/// every return receipt, linked (task P2-T02) or unlinked (task P2-T03) alike, so the two flows
/// can never print two different descriptions of the one policy they both enforce (SRS NFR-L3).
/// </summary>
internal static class ReturnPolicyTextBuilder
{
    public static async Task<string> BuildAsync(
        ISettings settings, ICategoryStore categories, CancellationToken cancellationToken)
    {
        var policy = settings.Policy;
        var names = new List<string>(policy.NonReturnableCategoryIds.Count);

        foreach (var categoryId in policy.NonReturnableCategoryIds)
        {
            var category = await categories.FindByIdAsync(categoryId, cancellationToken).ConfigureAwait(false);
            if (category is not null)
            {
                names.Add(category.Name);
            }
        }

        return ReturnPolicyTextFormatter.Describe(policy, names);
    }
}

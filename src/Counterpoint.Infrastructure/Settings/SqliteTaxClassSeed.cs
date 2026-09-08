using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Settings;

/// <summary>
/// Creates the shop's tax classes at first run (SRS FR-10.3, Q-02).
/// </summary>
/// <remarks>
/// Guarded on the class name, so running the wizard twice adds nothing, and an existing class
/// keeps its rate: a rate the shop has already sold and reported against is not a seeder's to
/// change. Editing one is P1-T04's reference-data screen, which knows to write a price-change
/// style record when it does.
/// </remarks>
internal sealed class SqliteTaxClassSeed : ITaxClassSeed
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteTaxClassSeed(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> EnsureAsync(
        string name,
        TaxRate rate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var existing = await context.Set<TaxClass>()
                    .Where(row => row.Name == trimmed)
                    .Select(row => (long?)row.Id)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    return existing.Value;
                }

                var created = new TaxClass { Name = trimmed, Rate = rate, Active = true };

                context.Add(created);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return created.Id;
            },
            cancellationToken);
    }
}

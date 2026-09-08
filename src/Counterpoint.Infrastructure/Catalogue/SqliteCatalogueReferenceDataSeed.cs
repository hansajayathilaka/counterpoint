using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// The default unit and category set the first-run wizard puts into an empty database
/// (docs/01_DATA_MODEL.md §11).
/// </summary>
/// <remarks>
/// Guarded row by row on name, exactly as <c>FirstRunSeeder</c>'s "Piece" and "Exempt" rows are:
/// <c>FirstRunSeeder</c> runs on every start and already creates a "Piece" unit before this seed
/// ever gets a turn, so <see cref="EnsureDefaultUomsAsync"/> completes that same row rather than
/// creating a second "Piece" - matching the pattern <c>SqliteTaxClassSeed</c> uses for "Exempt".
/// </remarks>
internal sealed class SqliteCatalogueReferenceDataSeed : ICatalogueReferenceDataSeed
{
    private static readonly IReadOnlyList<(string Name, string Symbol, int DecimalPlaces)> DefaultUoms =
    [
        ("Piece", "pc", 0),
        ("Metre", "m", 3),
        ("Kilogram", "kg", 3),
        ("Litre", "L", 3),
        ("Box", "box", 0),
        ("Coil", "coil", 0),
        ("Packet", "pkt", 0),
        ("Roll", "roll", 0),
        ("Bundle", "bdl", 0),
    ];

    private static readonly IReadOnlyList<string> DefaultCategories =
    [
        "Plumbing", "Electrical", "Fasteners", "Tools", "Paint", "Adhesives", "Garden", "Building",
    ];

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteCatalogueReferenceDataSeed(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task EnsureDefaultUomsAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                using var context = _unitOfWork.CreateDbContext();

                foreach (var (name, symbol, decimalPlaces) in DefaultUoms)
                {
                    if (await context.Set<Uom>().AnyAsync(row => row.Name == name, token).ConfigureAwait(false))
                    {
                        continue;
                    }

                    context.Add(new Uom { Name = name, Symbol = symbol, DecimalPlaces = decimalPlaces });
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public Task EnsureDefaultCategoriesAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                using var context = _unitOfWork.CreateDbContext();

                foreach (var name in DefaultCategories)
                {
                    var exists = await context.Set<Category>()
                        .AnyAsync(row => row.Name == name && row.ParentId == null, token)
                        .ConfigureAwait(false);

                    if (exists)
                    {
                        continue;
                    }

                    context.Add(new Category { Name = name, ParentId = null, Active = true });
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }
            },
            cancellationToken);
}

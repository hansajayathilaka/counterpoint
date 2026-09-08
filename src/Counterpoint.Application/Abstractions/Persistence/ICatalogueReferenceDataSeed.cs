using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Creates the shop's default units of measure and top-level categories at first run
/// (docs/01_DATA_MODEL.md §11).
/// </summary>
/// <remarks>
/// <para>
/// Seeding, not maintenance - the same split as <see cref="ITaxClassSeed"/>. Renaming, adding or
/// deactivating a unit or a category afterwards is <c>P1-T04</c>'s reference-data screens
/// (<c>IUomMaintenance</c>, <c>ICategoryMaintenance</c>); this exists only to put the documented
/// starting set into an empty database.
/// </para>
/// <para>
/// Guarded row by row on natural key (name), exactly as <c>FirstRunSeeder</c>'s "Piece" and
/// "Exempt" rows are, so this composes safely alongside that seeder rather than racing it or
/// duplicating what it already wrote.
/// </para>
/// </remarks>
public interface ICatalogueReferenceDataSeed
{
    /// <summary>
    /// Ensures the default unit set exists: Piece, Metre, Kilogram, Litre, Box, Coil, Packet,
    /// Roll, Bundle (docs/01_DATA_MODEL.md §11). A unit that is already there, under any name in
    /// the set, is left alone.
    /// </summary>
    public Task EnsureDefaultUomsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the default top-level category set exists: Plumbing, Electrical, Fasteners, Tools,
    /// Paint, Adhesives, Garden, Building (docs/01_DATA_MODEL.md §11).
    /// </summary>
    public Task EnsureDefaultCategoriesAsync(CancellationToken cancellationToken = default);
}

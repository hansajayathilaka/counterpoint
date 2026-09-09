using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Import;

/// <summary>
/// Spreadsheet catalogue import and export (SRS FR-2.22, FR-2.23, AC-07, Q-08). Owner only, the
/// same as every other catalogue-administration surface (SRS §3.3 ROLE-2, NFR-S2, AC-17) - a bad
/// import touches every product in the shop at once, which is exactly the kind of change a
/// cashier session must not be able to make.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dry run is not optional.</b> <see cref="PreviewAsync"/> and <see cref="CommitAsync"/> build
/// the identical row-by-row plan from the same file and mapping; only <see cref="CommitAsync"/>
/// writes it, and only when the plan contains zero errors. A file with even one bad row commits
/// nothing at all - there is no partial import to unwind, because there is no partial import
/// (docs/03_PHASE_1_core_trading.md P1-T13, "a bad import is very hard to unwind").
/// </para>
/// <para>
/// <b>One row, one product's defining variant.</b> A spreadsheet import creates or updates exactly
/// one <c>product</c> and one <c>product_variant</c> per row, using the row's code as both
/// <c>product.code</c> and the variant's <c>sku</c>. A product that needs more than one variant -
/// a bolt in three finishes - is still built through the product editor's variant matrix (P1-T05);
/// a bulk catalogue load is not the tool for that, and folding it in here would need an attributes
/// column this task's SRS references (FR-2.22, FR-2.23) do not ask for.
/// </para>
/// <para>
/// <b>The quantity column is a target, not an increment.</b> <see cref="ImportColumnMapping.Qty"/>
/// names the quantity on hand <em>as of the file</em>; the importer posts only the difference
/// between that and the variant's current balance, as one <c>OPENING</c> movement. Re-importing an
/// unchanged export therefore posts nothing, which is what makes "export, then re-import" round
/// trip cleanly rather than doubling every balance in the shop.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ICatalogueImportService
{
    /// <summary>
    /// Reads <paramref name="filePath"/>, builds the full row-by-row plan, and returns its counts
    /// and a bounded sample of each bucket. Writes nothing.
    /// </summary>
    public Task<ImportPreviewReport> PreviewAsync(
        string filePath,
        ImportColumnMapping mapping,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the same plan <see cref="PreviewAsync"/> would from the same file and mapping, and -
    /// only when it contains no errors - writes every create and update, posts every opening-stock
    /// delta, and attaches every new barcode, all in one transaction (CLAUDE.md invariant 3).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The plan contains one or more row errors. Nothing is written; call <see cref="PreviewAsync"/>
    /// first to see what needs fixing.
    /// </exception>
    public Task<ImportCommitResult> CommitAsync(
        string filePath,
        ImportColumnMapping mapping,
        CancellationToken cancellationToken = default);

    /// <summary>Every column-mapping profile the shop has saved, most recently saved first.</summary>
    public Task<IReadOnlyList<ImportMappingProfile>> ListMappingProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves a mapping profile under its name, replacing any existing profile of the same name.</summary>
    public Task SaveMappingProfileAsync(ImportMappingProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved mapping profile. Always succeeds, even if no profile has that name.</summary>
    public Task DeleteMappingProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every active product's catalogue fields, current price and current stock to
    /// <paramref name="filePath"/>, in the column shape <see cref="ImportColumnMapping.Default"/>
    /// expects back (SRS FR-2.23).
    /// </summary>
    public Task ExportCatalogueAsync(string filePath, CancellationToken cancellationToken = default);
}

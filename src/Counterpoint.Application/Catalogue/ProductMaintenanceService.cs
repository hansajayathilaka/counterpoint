using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// The owner's product, variant and unit-of-measure maintenance - the defining feature of
/// hardware retail (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
/// <remarks>
/// Internal, for the same reason every other catalogue maintenance service is: the role check on
/// <see cref="IProductMaintenance"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </remarks>
internal sealed class ProductMaintenanceService : IProductMaintenance
{
    private readonly IProductStore _products;
    private readonly ICategoryStore _categories;
    private readonly IBrandStore _brands;
    private readonly IUomStore _uoms;
    private readonly ITaxClassStore _taxClasses;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public ProductMaintenanceService(
        IProductStore products,
        ICategoryStore categories,
        IBrandStore brands,
        IUomStore uoms,
        ITaxClassStore taxClasses,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(brands);
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(taxClasses);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _products = products;
        _categories = categories;
        _brands = brands;
        _uoms = uoms;
        _taxClasses = taxClasses;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _products.ListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ProductRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _products.FindByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveProductCommand command, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(command, excludingId: null, cancellationToken).ConfigureAwait(false);

        if (!validated.ConfirmDuplicate)
        {
            var similar = await FindSimilarProductsAsync(
                validated.Name, validated.BrandId, size: null, excludingProductId: null, cancellationToken)
                .ConfigureAwait(false);

            if (similar.Count > 0)
            {
                throw new DuplicateProductWarningException(similar);
            }
        }

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _products.CreateAsync(validated, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created,
                    CatalogueAuditActions.ProductEntityType,
                    id,
                    now,
                    before: null,
                    after: ProductJson(validated),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveProductCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireProductAsync(id, cancellationToken).ConfigureAwait(false);

        if (command.BaseUomId != existing.BaseUomId)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{existing.Name}' already holds stock in {existing.BaseUomSymbol}. The base unit cannot be changed once a product exists - every quantity on the books is already expressed in it."));
        }

        var validated = await ValidateAsync(command, excludingId: id, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _products.UpdateAsync(id, validated, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    CatalogueAuditActions.ProductEntityType,
                    id,
                    now,
                    before: ProductJson(existing),
                    after: ProductJson(validated),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireProductAsync(id, cancellationToken).ConfigureAwait(false);

        if (!existing.Active)
        {
            return;
        }

        await SetProductActiveAsync(id, existing.Name, active: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireProductAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing.Active)
        {
            return;
        }

        await SetProductActiveAsync(id, existing.Name, active: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductVariantRecord>> ListVariantsAsync(long productId, CancellationToken cancellationToken = default) =>
        _products.ListVariantsAsync(productId, cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateVariantAsync(long productId, SaveProductVariantCommand command, CancellationToken cancellationToken = default)
    {
        await RequireProductAsync(productId, cancellationToken).ConfigureAwait(false);
        var validated = await ValidateVariantAsync(command, excludingId: null, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _products.CreateVariantAsync(productId, validated, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created,
                    CatalogueAuditActions.ProductVariantEntityType,
                    id,
                    now,
                    before: null,
                    after: VariantJson(validated),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateVariantAsync(long variantId, SaveProductVariantCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireVariantAsync(variantId, cancellationToken).ConfigureAwait(false);
        var validated = await ValidateVariantAsync(command, excludingId: variantId, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _products.UpdateVariantAsync(variantId, validated, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    CatalogueAuditActions.ProductVariantEntityType,
                    variantId,
                    now,
                    before: VariantJson(new SaveProductVariantCommand(existing.Sku, existing.Attributes, existing.Price)),
                    after: VariantJson(validated),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateVariantAsync(long variantId, CancellationToken cancellationToken = default)
    {
        var existing = await RequireVariantAsync(variantId, cancellationToken).ConfigureAwait(false);

        if (!existing.Active)
        {
            return;
        }

        await SetVariantActiveAsync(variantId, existing.Sku, active: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReactivateVariantAsync(long variantId, CancellationToken cancellationToken = default)
    {
        var existing = await RequireVariantAsync(variantId, cancellationToken).ConfigureAwait(false);

        if (existing.Active)
        {
            return;
        }

        await SetVariantActiveAsync(variantId, existing.Sku, active: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductUomRecord>> ListUomOptionsAsync(long productId, CancellationToken cancellationToken = default) =>
        _products.ListUomOptionsAsync(productId, cancellationToken);

    /// <inheritdoc />
    public async Task<long> AddUomOptionAsync(long productId, SaveProductUomCommand command, CancellationToken cancellationToken = default)
    {
        var product = await RequireProductAsync(productId, cancellationToken).ConfigureAwait(false);
        await ValidateUomOptionAsync(product, command, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _products.AddUomOptionAsync(productId, command, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created,
                    CatalogueAuditActions.ProductUomEntityType,
                    id,
                    now,
                    before: null,
                    after: UomOptionJson(command),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateUomOptionAsync(long uomOptionId, SaveProductUomCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireUomOptionAsync(uomOptionId, cancellationToken).ConfigureAwait(false);
        RequireNotBaseUnit(existing);

        var product = await RequireProductAsync(existing.ProductId, cancellationToken).ConfigureAwait(false);
        await ValidateUomOptionAsync(product, command, cancellationToken, excludingOptionId: uomOptionId).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _products.UpdateUomOptionAsync(uomOptionId, command, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    CatalogueAuditActions.ProductUomEntityType,
                    uomOptionId,
                    now,
                    before: UomOptionJson(new SaveProductUomCommand(existing.UomId, existing.Conversion, existing.SellingPrice)),
                    after: UomOptionJson(command),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveUomOptionAsync(long uomOptionId, CancellationToken cancellationToken = default)
    {
        var existing = await RequireUomOptionAsync(uomOptionId, cancellationToken).ConfigureAwait(false);
        RequireNotBaseUnit(existing);

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await _products.RemoveUomOptionAsync(uomOptionId, token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "This unit is referenced elsewhere and cannot be removed. Deactivate the product instead.");
                }

                await RecordAsync(
                    CatalogueAuditActions.Deleted,
                    CatalogueAuditActions.ProductUomEntityType,
                    uomOptionId,
                    now,
                    before: UomOptionJson(new SaveProductUomCommand(existing.UomId, existing.Conversion, existing.SellingPrice)),
                    after: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VariantMatrixPreview> PreviewVariantMatrixAsync(
        long productId,
        IReadOnlyList<VariantAxis> axes,
        CancellationToken cancellationToken = default)
    {
        await RequireProductAsync(productId, cancellationToken).ConfigureAwait(false);

        var existing = await ExistingAttributeSetAsync(productId, cancellationToken).ConfigureAwait(false);
        var toCreate = VariantMatrixGenerator.Generate(axes, existing);
        var totalCombinations = axes.Aggregate(1, (running, axis) => running * axis.Values.Count);

        return new VariantMatrixPreview(toCreate, totalCombinations - toCreate.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> CommitVariantMatrixAsync(
        long productId,
        IReadOnlyList<VariantAttributes> combinations,
        Money defaultPrice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(combinations);

        var product = await RequireProductAsync(productId, cancellationToken).ConfigureAwait(false);

        if (defaultPrice.IsNegative)
        {
            throw new InvalidOperationException("A variant's price cannot be negative.");
        }

        // Re-checked against the current state, not just the preview's: another edit could have
        // added one of these combinations since the preview was shown.
        var existing = await ExistingAttributeSetAsync(productId, cancellationToken).ConfigureAwait(false);
        var stillToCreate = combinations.Where(existing.Add).ToArray();

        if (stillToCreate.Length == 0)
        {
            return [];
        }

        var takenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in await _products.ListVariantsAsync(productId, cancellationToken).ConfigureAwait(false))
        {
            takenSkus.Add(variant.Sku);
        }

        var commands = stillToCreate
            .Select(combination => new SaveProductVariantCommand(
                VariantSkuGenerator.Generate(product.Code, combination, takenSkus),
                combination.ToDictionary(),
                defaultPrice))
            .ToArray();

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var ids = await _products.CreateVariantsAsync(productId, commands, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.VariantMatrixCommitted,
                    CatalogueAuditActions.ProductEntityType,
                    productId,
                    now,
                    before: null,
                    after: SecurityAuditJson.Object(
                        ("variant_count", (long)commands.Length),
                        ("skus", string.Join(',', commands.Select(c => c.Sku)))),
                    token).ConfigureAwait(false);

                return ids;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SimilarProductMatch>> FindSimilarProductsAsync(
        string name,
        long? brandId,
        string? size = null,
        long? excludingProductId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return [];
        }

        var candidates = await _products.ListForDuplicateCheckAsync(cancellationToken).ConfigureAwait(false);
        var matches = new List<SimilarProductMatch>();

        foreach (var candidate in candidates)
        {
            if (candidate.Id == excludingProductId || candidate.BrandId != brandId)
            {
                continue;
            }

            var score = ProductNameSimilarity.Score(trimmedName, candidate.Name);
            if (score < ProductNameSimilarity.DefaultThreshold)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(size))
            {
                var variants = await _products.ListVariantsAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
                var hasMatchingSize = variants.Any(variant => variant.Attributes.Values.Any(
                    value => string.Equals(value, size, StringComparison.OrdinalIgnoreCase)));

                if (!hasMatchingSize)
                {
                    continue;
                }
            }

            matches.Add(new SimilarProductMatch(candidate.Id, candidate.Name, candidate.BrandName, score));
        }

        return [.. matches.OrderByDescending(match => match.Score)];
    }

    private async Task<HashSet<VariantAttributes>> ExistingAttributeSetAsync(long productId, CancellationToken cancellationToken)
    {
        var variants = await _products.ListVariantsAsync(productId, cancellationToken).ConfigureAwait(false);

        return variants
            .Where(variant => variant.Attributes.Count > 0)
            .Select(variant => new VariantAttributes(variant.Attributes))
            .ToHashSet();
    }

    private async Task SetProductActiveAsync(long id, string name, bool active, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _products.SetActiveAsync(id, active, token).ConfigureAwait(false);

                await RecordAsync(
                    active ? CatalogueAuditActions.Reactivated : CatalogueAuditActions.Deactivated,
                    CatalogueAuditActions.ProductEntityType,
                    id,
                    now,
                    before: SecurityAuditJson.Object(("name", name), ("active", !active)),
                    after: SecurityAuditJson.Object(("name", name), ("active", active)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SetVariantActiveAsync(long variantId, string sku, bool active, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _products.SetVariantActiveAsync(variantId, active, token).ConfigureAwait(false);

                await RecordAsync(
                    active ? CatalogueAuditActions.Reactivated : CatalogueAuditActions.Deactivated,
                    CatalogueAuditActions.ProductVariantEntityType,
                    variantId,
                    now,
                    before: SecurityAuditJson.Object(("sku", sku), ("active", !active)),
                    after: SecurityAuditJson.Object(("sku", sku), ("active", active)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SaveProductCommand> ValidateAsync(SaveProductCommand command, long? excludingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = RequireText(command.Code, "code");
        var name = RequireText(command.Name, "name");

        if (await _products.ExistsWithCodeAsync(code, excludingId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is already a product with code '{code}'. Pick another code."));
        }

        if (command.CategoryId is { } categoryId
            && await _categories.FindByIdAsync(categoryId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no category with id {categoryId}."));
        }

        if (command.BrandId is { } brandId
            && await _brands.FindByIdAsync(brandId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no brand with id {brandId}."));
        }

        var baseUom = await _uoms.FindByIdAsync(command.BaseUomId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no unit with id {command.BaseUomId} to use as the base unit."));

        if (!baseUom.Active)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{baseUom.Name}' is turned off and cannot be used as a base unit. Turn it back on first."));
        }

        if (await _taxClasses.FindByIdAsync(command.TaxClassId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no tax class with id {command.TaxClassId}."));
        }

        return command with { Code = code, Name = name };
    }

    private async Task<SaveProductVariantCommand> ValidateVariantAsync(
        SaveProductVariantCommand command,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = RequireText(command.Sku, "SKU");

        if (await _products.ExistsWithSkuAsync(sku, excludingId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is already a variant with SKU '{sku}'. Pick another SKU."));
        }

        if (command.Price.IsNegative)
        {
            throw new InvalidOperationException("A variant's price cannot be negative.");
        }

        return command with { Sku = sku };
    }

    private async Task ValidateUomOptionAsync(
        ProductRecord product,
        SaveProductUomCommand command,
        CancellationToken cancellationToken,
        long? excludingOptionId = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UomId == product.BaseUomId)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{product.Name}' already sells in {product.BaseUomSymbol} as its base unit. Add a different unit instead."));
        }

        var uom = await _uoms.FindByIdAsync(command.UomId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no unit with id {command.UomId}."));

        if (!uom.Active)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{uom.Name}' is turned off. Turn it back on before adding it to a product."));
        }

        var options = await _products.ListUomOptionsAsync(product.Id, cancellationToken).ConfigureAwait(false);

        if (options.Any(option => option.UomId == command.UomId && option.Id != excludingOptionId))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{product.Name}' already sells in {uom.Symbol}."));
        }
    }

    private async Task<ProductRecord> RequireProductAsync(long id, CancellationToken cancellationToken) =>
        await _products.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"There is no product with id {id}. It may have been removed since this screen was opened."));

    private async Task<ProductVariantRecord> RequireVariantAsync(long variantId, CancellationToken cancellationToken) =>
        await _products.FindVariantByIdAsync(variantId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"There is no variant with id {variantId}. It may have been removed since this screen was opened."));

    private async Task<ProductUomRecord> RequireUomOptionAsync(long uomOptionId, CancellationToken cancellationToken) =>
        await _products.FindUomOptionByIdAsync(uomOptionId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"There is no product unit with id {uomOptionId}. It may have been removed since this screen was opened."));

    private static void RequireNotBaseUnit(ProductUomRecord option)
    {
        if (option.IsBase)
        {
            throw new InvalidOperationException(
                "This is the product's base unit. It was created with the product and cannot be edited or removed here.");
        }
    }

    private static string RequireText(string value, string what)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A product needs a {what}."));
        }

        return trimmed;
    }

    private static string ProductJson(SaveProductCommand command) => SecurityAuditJson.Object(
        ("code", command.Code),
        ("name", command.Name),
        ("type", ProductTypes.ToToken(command.Type)),
        ("base_uom_id", command.BaseUomId),
        ("tax_class_id", command.TaxClassId),
        ("category_id", command.CategoryId?.ToString(CultureInfo.InvariantCulture)),
        ("brand_id", command.BrandId?.ToString(CultureInfo.InvariantCulture)));

    private static string ProductJson(ProductRecord product) => SecurityAuditJson.Object(
        ("code", product.Code),
        ("name", product.Name),
        ("type", ProductTypes.ToToken(product.Type)),
        ("base_uom_id", product.BaseUomId),
        ("tax_class_id", product.TaxClassId),
        ("category_id", product.CategoryId?.ToString(CultureInfo.InvariantCulture)),
        ("brand_id", product.BrandId?.ToString(CultureInfo.InvariantCulture)));

    private static string VariantJson(SaveProductVariantCommand command) => SecurityAuditJson.Object(
        ("sku", command.Sku),
        ("price", command.Price.ToScaled()),
        ("attributes", string.Join(';', command.Attributes.Select(pair => pair.Key + "=" + pair.Value))));

    private static string UomOptionJson(SaveProductUomCommand command) => SecurityAuditJson.Object(
        ("uom_id", command.UomId),
        ("conversion_factor", command.Conversion.ToScaled()),
        ("selling_price", command.SellingPrice?.ToScaled().ToString(CultureInfo.InvariantCulture)));

    private Task RecordAsync(
        string action,
        string entityType,
        long entityId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Product maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, entityType, entityId, before, after),
            cancellationToken);
    }
}

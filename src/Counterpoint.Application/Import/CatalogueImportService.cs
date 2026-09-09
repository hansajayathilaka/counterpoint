using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Import;

/// <summary>
/// Spreadsheet catalogue import and export (SRS FR-2.22, FR-2.23, AC-07, Q-08).
/// </summary>
/// <remarks>
/// Internal, for the same reason every other catalogue maintenance service is: the role check on
/// <see cref="ICatalogueImportService"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </remarks>
internal sealed class CatalogueImportService : ICatalogueImportService
{
    /// <summary>
    /// Not a strongly-typed <c>ISettings</c> group: that framework is a fixed set of FR-10 groups,
    /// and a shop's list of saved mapping profiles is open-ended. Read and written directly
    /// through <see cref="ISettingStore"/> instead, the same pattern
    /// <c>BarcodeMaintenanceService</c> uses for its internal-barcode prefix.
    /// </summary>
    private const string MappingProfilesSettingKey = "import.mapping_profiles";

    /// <summary>
    /// Allowed by <c>app_setting.value_type</c>'s CHECK constraint but unused by the FR-10
    /// settings framework, which keeps every one of its own keys to one scalar per row
    /// (docs/01_DATA_MODEL.md §8). A list of named mapping profiles is exactly the shape that
    /// framework deliberately does not carry, so this is the one place in the codebase this token
    /// is used.
    /// </summary>
    private const string JsonValueType = "JSON";

    private const string ImportedAuditAction = "CATALOGUE_IMPORTED";

    /// <summary><c>stock_movement.ref_doc_type</c> for every opening-balance delta an import posts.</summary>
    private const string ImportRefDocType = "IMPORT";

    /// <summary>The most rows a bucket's dry-run sample carries, so a 20 000-row file's preview DTO stays small.</summary>
    private const int SampleSize = 20;

    private readonly ISpreadsheetReader _reader;
    private readonly ISpreadsheetWriter _writer;
    private readonly ICatalogueExportQuery _exportQuery;
    private readonly IProductStore _products;
    private readonly ICategoryStore _categories;
    private readonly IBrandStore _brands;
    private readonly IUomStore _uoms;
    private readonly ITaxClassStore _taxClasses;
    private readonly IBarcodeStore _barcodes;
    private readonly IStockPositionReader _stockPositions;
    private readonly IProductMaintenance _productMaintenance;
    private readonly IBarcodeMaintenance _barcodeMaintenance;
    private readonly ICategoryMaintenance _categoryMaintenance;
    private readonly IBrandMaintenance _brandMaintenance;
    private readonly IStockLedger _stockLedger;
    private readonly IAuditTrail _audit;
    private readonly ISettingStore _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public CatalogueImportService(
        ISpreadsheetReader reader,
        ISpreadsheetWriter writer,
        ICatalogueExportQuery exportQuery,
        IProductStore products,
        ICategoryStore categories,
        IBrandStore brands,
        IUomStore uoms,
        ITaxClassStore taxClasses,
        IBarcodeStore barcodes,
        IStockPositionReader stockPositions,
        IProductMaintenance productMaintenance,
        IBarcodeMaintenance barcodeMaintenance,
        ICategoryMaintenance categoryMaintenance,
        IBrandMaintenance brandMaintenance,
        IStockLedger stockLedger,
        IAuditTrail audit,
        ISettingStore settings,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(exportQuery);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(brands);
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(taxClasses);
        ArgumentNullException.ThrowIfNull(barcodes);
        ArgumentNullException.ThrowIfNull(stockPositions);
        ArgumentNullException.ThrowIfNull(productMaintenance);
        ArgumentNullException.ThrowIfNull(barcodeMaintenance);
        ArgumentNullException.ThrowIfNull(categoryMaintenance);
        ArgumentNullException.ThrowIfNull(brandMaintenance);
        ArgumentNullException.ThrowIfNull(stockLedger);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _reader = reader;
        _writer = writer;
        _exportQuery = exportQuery;
        _products = products;
        _categories = categories;
        _brands = brands;
        _uoms = uoms;
        _taxClasses = taxClasses;
        _barcodes = barcodes;
        _stockPositions = stockPositions;
        _productMaintenance = productMaintenance;
        _barcodeMaintenance = barcodeMaintenance;
        _categoryMaintenance = categoryMaintenance;
        _brandMaintenance = brandMaintenance;
        _stockLedger = stockLedger;
        _audit = audit;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<ImportPreviewReport> PreviewAsync(
        string filePath,
        ImportColumnMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var plans = await BuildPlanAsync(filePath, mapping, cancellationToken).ConfigureAwait(false);
        return BuildPreview(plans);
    }

    /// <inheritdoc />
    public async Task<ImportCommitResult> CommitAsync(
        string filePath,
        ImportColumnMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var plans = await BuildPlanAsync(filePath, mapping, cancellationToken).ConfigureAwait(false);
        var counts = Count(plans);

        if (counts.Errors > 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"This file has {counts.Errors} row(s) that fail validation. Nothing was written - fix them and try again."));
        }

        var actor = RequireActor();
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var newCategoryIds = new Dictionary<string, long>(StringComparer.Ordinal);
                var newBrandIds = new Dictionary<string, long>(StringComparer.Ordinal);

                foreach (var plan in plans)
                {
                    if (plan.Outcome is ImportRowOutcome.Skip)
                    {
                        continue;
                    }

                    await CommitRowAsync(plan, newCategoryIds, newBrandIds, actor.Id, now, token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        ImportedAuditAction,
                        CatalogueAuditActions.ProductEntityType,
                        EntityId: null,
                        AfterJson: SecurityAuditJson.Object(
                            ("file", System.IO.Path.GetFileName(filePath)),
                            ("creates", (long)counts.Creates),
                            ("updates", (long)counts.Updates),
                            ("skips", (long)counts.Skips))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new ImportCommitResult(counts);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportMappingProfile>> ListMappingProfilesAsync(CancellationToken cancellationToken = default) =>
        await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveMappingProfileAsync(ImportMappingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = profile.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("A mapping profile needs a name.");
        }

        var profiles = (await LoadProfilesAsync(cancellationToken).ConfigureAwait(false))
            .Where(existing => !string.Equals(existing.Name, name, StringComparison.Ordinal))
            .Append(profile with { Name = name })
            .ToArray();

        await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteMappingProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var profiles = (await LoadProfilesAsync(cancellationToken).ConfigureAwait(false))
            .Where(existing => !string.Equals(existing.Name, name, StringComparison.Ordinal))
            .ToArray();

        await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ExportCatalogueAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var rows = await _exportQuery.ListAllAsync(cancellationToken).ConfigureAwait(false);

        var table = new SpreadsheetTable(
            CatalogueExportColumns.Headers,
            [.. rows.Select(ToExportRow)]);

        await _writer.WriteAsync(filePath, table, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string?> ToExportRow(CatalogueExportRow row) =>
    [
        row.Code,
        row.Name,
        row.NameAlt,
        row.CategoryName,
        row.BrandName,
        row.UomName,
        ProductTypes.ToToken(row.Type),
        row.TaxClassName,
        row.Location,
        row.NonReturnable ? "Y" : "N",
        row.WarrantyDays?.ToString(CultureInfo.InvariantCulture),
        row.Notes,
        row.PrimaryBarcode,
        row.Price.Amount.ToString(CultureInfo.InvariantCulture),
        row.Cost.Amount.ToString(CultureInfo.InvariantCulture),
        row.QtyOnHand.Value.ToString(CultureInfo.InvariantCulture),
    ];

    // ------------------------------------------------------------------
    // Plan building - shared, unchanged, between PreviewAsync and CommitAsync.
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<RowPlan>> BuildPlanAsync(
        string filePath,
        ImportColumnMapping mapping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(mapping);

        var table = await _reader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        var columns = ResolveColumns(table.Headers, mapping);

        var categoriesByName = (await _categories.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(category => category.ParentId is null)
            .ToDictionary(category => category.Name, category => category.Id, StringComparer.Ordinal);

        var brandsByName = (await _brands.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(brand => brand.Name, brand => brand.Id, StringComparer.Ordinal);

        var uoms = await _uoms.ListAsync(cancellationToken).ConfigureAwait(false);

        var taxClassesByName = (await _taxClasses.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(taxClass => taxClass.Name, taxClass => taxClass, StringComparer.Ordinal);

        var existingProducts = await _products.ListAsync(cancellationToken).ConfigureAwait(false);
        var existingIdsByCode = existingProducts.ToDictionary(product => product.Code, product => product.Id, StringComparer.Ordinal);

        var plans = new List<RowPlan>(table.Rows.Count);

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var rowNumber = i + 2; // row 1 is the header
            var row = table.Rows[i];

            if (row.All(cell => string.IsNullOrWhiteSpace(cell)))
            {
                plans.Add(RowPlan.BlankRow(rowNumber));
                continue;
            }

            plans.Add(await ParseRowAsync(
                rowNumber, row, columns, categoriesByName, brandsByName, uoms, taxClassesByName, existingIdsByCode, cancellationToken)
                .ConfigureAwait(false));
        }

        ApplyWithinFileDuplicateChecks(plans);

        return plans;
    }

    private async Task<RowPlan> ParseRowAsync(
        int rowNumber,
        IReadOnlyList<string?> row,
        ResolvedColumns columns,
        Dictionary<string, long> categoriesByName,
        Dictionary<string, long> brandsByName,
        IReadOnlyList<UomRecord> uoms,
        Dictionary<string, TaxClassRecord> taxClassesByName,
        Dictionary<string, long> existingIdsByCode,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var code = ColumnIndexResolver.Cell(row, columns.Code);
        if (string.IsNullOrEmpty(code))
        {
            errors.Add("Code is required.");
        }

        var name = ColumnIndexResolver.Cell(row, columns.Name);
        if (string.IsNullOrEmpty(name))
        {
            errors.Add("Name is required.");
        }

        // An unknown category or brand name is not an error - FR-2.22's "offer to create" - so
        // ResolvedCategoryId/ResolvedBrandId stay null and CommitAsync creates it. Only a blank
        // cell (CategoryName itself null) means "no category" and creates nothing.
        var categoryName = ColumnIndexResolver.Cell(row, columns.Category);
        long? categoryId = categoryName is not null && categoriesByName.TryGetValue(categoryName, out var foundCategoryId)
            ? foundCategoryId
            : null;

        var brandName = ColumnIndexResolver.Cell(row, columns.Brand);
        long? brandId = brandName is not null && brandsByName.TryGetValue(brandName, out var foundBrandId)
            ? foundBrandId
            : null;

        var unitText = ColumnIndexResolver.Cell(row, columns.Unit);
        UomRecord? uom = null;
        if (string.IsNullOrEmpty(unitText))
        {
            errors.Add("Unit is required.");
        }
        else
        {
            uom = uoms.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, unitText, StringComparison.Ordinal)
                || string.Equals(candidate.Symbol, unitText, StringComparison.Ordinal));

            if (uom is null)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"There is no unit called '{unitText}'."));
            }
            else if (!uom.Active)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"'{unitText}' is turned off and cannot be used."));
                uom = null;
            }
        }

        var typeText = ColumnIndexResolver.Cell(row, columns.Type);
        var productType = ProductType.Standard;
        if (!string.IsNullOrEmpty(typeText) && !TryParseProductType(typeText, out productType, out var typeError))
        {
            errors.Add(typeError!);
        }

        var taxClassText = ColumnIndexResolver.Cell(row, columns.TaxClass);
        long? taxClassId = null;
        if (string.IsNullOrEmpty(taxClassText))
        {
            errors.Add("Tax Class is required.");
        }
        else if (taxClassesByName.TryGetValue(taxClassText, out var taxClass))
        {
            if (!taxClass.Active)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"'{taxClassText}' is turned off and cannot be used."));
            }
            else
            {
                taxClassId = taxClass.Id;
            }
        }
        else
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"There is no tax class called '{taxClassText}'."));
        }

        var location = ColumnIndexResolver.Cell(row, columns.Location);
        var nonReturnable = ParseBoolish(ColumnIndexResolver.Cell(row, columns.NonReturnable));

        int? warrantyDays = null;
        var warrantyText = ColumnIndexResolver.Cell(row, columns.WarrantyDays);
        if (!string.IsNullOrEmpty(warrantyText))
        {
            if (int.TryParse(warrantyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= 0)
            {
                warrantyDays = days;
            }
            else
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"'{warrantyText}' is not a whole number of warranty days."));
            }
        }

        var notes = ColumnIndexResolver.Cell(row, columns.Notes);
        var barcode = ColumnIndexResolver.Cell(row, columns.Barcode);

        var price = Money.Zero;
        var priceText = ColumnIndexResolver.Cell(row, columns.Price);
        if (string.IsNullOrEmpty(priceText))
        {
            errors.Add("Price is required.");
        }
        else if (!TryParseNonNegativeMoney(priceText, out price, out var priceError))
        {
            errors.Add(priceError!);
        }

        var cost = Money.Zero;
        var costText = ColumnIndexResolver.Cell(row, columns.Cost);
        if (!string.IsNullOrEmpty(costText) && !TryParseNonNegativeMoney(costText, out cost, out var costError))
        {
            errors.Add(costError!);
        }

        if (cost.IsPositive && price <= cost && errors.Count == 0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"The price ({price}) is at or below the cost ({cost})."));
        }

        var qtyValue = 0m;
        var qtyText = ColumnIndexResolver.Cell(row, columns.Qty);
        if (!string.IsNullOrEmpty(qtyText)
            && (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out qtyValue) || qtyValue < 0m))
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"'{qtyText}' is not a valid quantity."));
            qtyValue = 0m;
        }

        Quantity? targetQty = null;
        if (uom is not null)
        {
            var probeProduct = new Product(
                id: 0,
                code: code ?? string.Empty,
                name: name ?? string.Empty,
                type: productType,
                baseUomId: uom.Id,
                uomOptions: [new ProductUomOption(uom.Id, uom.Symbol, uom.DecimalPlaces, UomConversion.Base, IsBase: true, SellingPrice: null)]);

            try
            {
                targetQty = UomConverter.ToBase(qtyValue, uom.Id, probeProduct);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        long? existingProductId = code is not null && existingIdsByCode.TryGetValue(code, out var foundProductId)
            ? foundProductId
            : null;

        ProductRecord? existingProduct = null;
        ProductVariantRecord? existingVariant = null;

        if (existingProductId is { } productId)
        {
            existingProduct = await _products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);

            if (existingProduct is not null && uom is not null && existingProduct.BaseUomId != uom.Id)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{code}' already holds stock in {existingProduct.BaseUomSymbol}. The unit column must say '{existingProduct.BaseUomSymbol}' - the base unit cannot be changed once a product exists."));
            }

            // A flat catalogue row updates the product's one defining variant, whatever its own
            // SKU happens to be - most products the ordinary product editor creates never set a
            // SKU equal to the product code, so matching on SKU here would miss them and create a
            // stray second variant instead of updating the one that exists. A product with more
            // than one variant (the variant-matrix case, P1-T05) is out of this row's reach - it
            // is reported, not guessed at.
            var variants = await _products.ListVariantsAsync(productId, cancellationToken).ConfigureAwait(false);
            if (variants.Count == 1)
            {
                existingVariant = variants[0];
            }
            else if (variants.Count > 1)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{code}' has more than one variant. Change it through the product editor, not a spreadsheet import."));
            }
        }

        if (!string.IsNullOrEmpty(barcode))
        {
            var conflict = await _barcodes.FindConflictAsync(barcode, cancellationToken).ConfigureAwait(false);
            if (conflict is not null && conflict.ProductVariantId != existingVariant?.Id)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Barcode '{barcode}' is already attached to '{conflict.ProductName}' ({conflict.Sku})."));
            }
        }

        return new RowPlan
        {
            RowNumber = rowNumber,
            IsBlank = false,
            Code = code,
            Name = name,
            NameAlt = ColumnIndexResolver.Cell(row, columns.NameAlt),
            CategoryName = categoryName,
            ResolvedCategoryId = categoryId,
            BrandName = brandName,
            ResolvedBrandId = brandId,
            ResolvedUomId = uom?.Id,
            ProductType = productType,
            ResolvedTaxClassId = taxClassId,
            Location = location,
            NonReturnable = nonReturnable,
            WarrantyDays = warrantyDays,
            Notes = notes,
            Barcode = barcode,
            Price = price,
            Cost = cost,
            TargetQty = targetQty,
            ExistingProductId = existingProductId,
            ExistingVariantId = existingVariant?.Id,
            ExistingVariantSku = existingVariant?.Sku,
            ExistingVariantAttributes = existingVariant?.Attributes,
            ExistingBaseUomId = existingProduct?.BaseUomId,
            ExistingMaxDiscountRate = existingProduct?.MaxDiscountRate,
            Errors = errors,
        };
    }

    private static void ApplyWithinFileDuplicateChecks(List<RowPlan> plans)
    {
        foreach (var group in plans
            .Where(plan => !plan.IsBlank && !string.IsNullOrEmpty(plan.Code))
            .GroupBy(plan => plan.Code!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var plan in group)
            {
                plan.Errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Code '{plan.Code}' appears more than once in this file."));
            }
        }

        foreach (var group in plans
            .Where(plan => !plan.IsBlank && !string.IsNullOrEmpty(plan.Barcode))
            .GroupBy(plan => plan.Barcode!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var plan in group)
            {
                plan.Errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Barcode '{plan.Barcode}' appears more than once in this file."));
            }
        }
    }

    // ------------------------------------------------------------------
    // Commit
    // ------------------------------------------------------------------

    private async Task CommitRowAsync(
        RowPlan plan,
        Dictionary<string, long> newCategoryIds,
        Dictionary<string, long> newBrandIds,
        long userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var categoryId = plan.ResolvedCategoryId
            ?? await GetOrCreateAsync(plan.CategoryName, newCategoryIds, CreateCategoryAsync, cancellationToken).ConfigureAwait(false);

        var brandId = plan.ResolvedBrandId
            ?? await GetOrCreateAsync(plan.BrandName, newBrandIds, CreateBrandAsync, cancellationToken).ConfigureAwait(false);

        long productId;
        long variantId;
        Quantity currentQty;

        if (plan.Outcome == ImportRowOutcome.Create)
        {
            productId = await _productMaintenance.CreateAsync(
                new SaveProductCommand(
                    plan.Code!,
                    plan.Name!,
                    plan.NameAlt,
                    categoryId,
                    brandId,
                    plan.ResolvedUomId!.Value,
                    plan.ProductType,
                    plan.ResolvedTaxClassId!.Value,
                    plan.Location,
                    plan.NonReturnable,
                    plan.WarrantyDays,
                    plan.Notes,
                    MaxDiscountRate: null,
                    ConfirmDuplicate: true),
                cancellationToken).ConfigureAwait(false);

            variantId = await _productMaintenance.CreateVariantAsync(
                productId,
                new SaveProductVariantCommand(plan.Code!, EmptyAttributes, plan.Price, ConfirmBelowCost: true),
                cancellationToken).ConfigureAwait(false);

            currentQty = Quantity.Zero(plan.ResolvedUomId.Value);
        }
        else
        {
            productId = plan.ExistingProductId!.Value;

            await _productMaintenance.UpdateAsync(
                productId,
                new SaveProductCommand(
                    plan.Code!,
                    plan.Name!,
                    plan.NameAlt,
                    categoryId,
                    brandId,
                    plan.ExistingBaseUomId!.Value,
                    plan.ProductType,
                    plan.ResolvedTaxClassId!.Value,
                    plan.Location,
                    plan.NonReturnable,
                    plan.WarrantyDays,
                    plan.Notes,
                    plan.ExistingMaxDiscountRate,
                    ConfirmDuplicate: true),
                cancellationToken).ConfigureAwait(false);

            if (plan.ExistingVariantId is { } existingVariantId)
            {
                // The existing variant's own SKU and attributes are carried through unchanged -
                // a flat catalogue row updates price and stock, never renames a SKU the product
                // editor gave the variant (see the comment on ParseRowAsync's variant match).
                await _productMaintenance.UpdateVariantAsync(
                    existingVariantId,
                    new SaveProductVariantCommand(
                        plan.ExistingVariantSku!,
                        plan.ExistingVariantAttributes ?? EmptyAttributes,
                        plan.Price,
                        ConfirmBelowCost: true),
                    cancellationToken).ConfigureAwait(false);

                variantId = existingVariantId;

                var position = await _stockPositions.FindAsync(variantId, cancellationToken).ConfigureAwait(false);

                // Deliberately not Quantity subtraction: IStockPositionReader's projection tags
                // the value with the variant id, not the unit of measure, so only .Value is safe
                // to trust here (a pre-existing P1-T07 read-side detail, not this task's to fix).
                currentQty = Quantity.FromDecimal(position?.QtyBase.Value ?? 0m, plan.ExistingBaseUomId.Value);
            }
            else
            {
                variantId = await _productMaintenance.CreateVariantAsync(
                    productId,
                    new SaveProductVariantCommand(plan.Code!, EmptyAttributes, plan.Price, ConfirmBelowCost: true),
                    cancellationToken).ConfigureAwait(false);

                currentQty = Quantity.Zero(plan.ExistingBaseUomId.Value);
            }
        }

        var barcode = plan.Barcode;
        if (!string.IsNullOrEmpty(barcode))
        {
            var conflict = await _barcodes.FindConflictAsync(barcode, cancellationToken).ConfigureAwait(false);
            if (conflict is null)
            {
                await _barcodeMaintenance.AddAsync(variantId, barcode, makePrimary: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Else: already attached to this same variant (the plan builder refused any other
            // conflict), so there is nothing to do.
        }

        // SERVICE and NON_INVENTORY products post no stock movement at all (FR-2.1-FR-2.8) -
        // the same rule CompleteSaleHandler applies on the sale path.
        if (!ProductTypes.PostsNoStockMovement(plan.ProductType) && plan.TargetQty is { } targetQty)
        {
            var deltaValue = targetQty.Value - currentQty.Value;
            if (deltaValue != 0m)
            {
                await _stockLedger.PostAsync(
                    new StockPosting(
                        variantId,
                        "OPENING",
                        Quantity.FromDecimal(deltaValue, targetQty.UomId),
                        plan.Cost,
                        ImportRefDocType,
                        RefDocId: null,
                        userId,
                        now),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The id a not-yet-resolved category or brand name gets: created once per commit and cached,
    /// so fifty rows of the same new category do not create it fifty times. Null in, null out - a
    /// blank cell stays "no category"/"no brand" rather than creating one named nothing.
    /// </summary>
    private static async Task<long?> GetOrCreateAsync(
        string? name,
        Dictionary<string, long> cache,
        Func<string, CancellationToken, Task<long>> create,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var id = await create(name, cancellationToken).ConfigureAwait(false);
        cache[name] = id;
        return id;
    }

    private Task<long> CreateCategoryAsync(string name, CancellationToken cancellationToken) =>
        _categoryMaintenance.CreateAsync(new SaveCategoryCommand(name, ParentId: null), cancellationToken);

    private Task<long> CreateBrandAsync(string name, CancellationToken cancellationToken) =>
        _brandMaintenance.CreateAsync(new SaveBrandCommand(name), cancellationToken);

    // ------------------------------------------------------------------
    // Mapping profiles
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<ImportMappingProfile>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        if (!settings.TryGetValue(MappingProfilesSettingKey, out var stored) || string.IsNullOrWhiteSpace(stored.Value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<ImportMappingProfile[]>(stored.Value) ?? [];
        }
        catch (JsonException)
        {
            // A row nothing here wrote (or a corrupted one) is a reason to start over, not to
            // refuse the screen that would let the owner fix it.
            return [];
        }
    }

    private async Task WriteProfilesAsync(ImportMappingProfile[] profiles, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var now = _timeProvider.GetLocalNow();
        var json = JsonSerializer.Serialize(profiles);

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _settings.WriteAsync(
                    [new SettingWrite(MappingProfilesSettingKey, json, JsonValueType, actor.Id)],
                    token).ConfigureAwait(false);

                // Same audit shape SettingsService/BarcodeMaintenanceService use for a raw
                // ISettingStore write (SRS FR-10.9, NFR-S8) - the profile list itself, not a
                // diff, since it is the whole named collection that changed.
                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        SettingsAuditActions.SettingChanged,
                        SettingsAuditActions.SettingEntityType,
                        EntityId: null,
                        AfterJson: SecurityAuditJson.Object(("key", MappingProfilesSettingKey), ("profile_count", (long)profiles.Length))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Small helpers
    // ------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static ResolvedColumns ResolveColumns(IReadOnlyList<string> headers, ImportColumnMapping mapping)
    {
        var code = ColumnIndexResolver.Resolve(headers, mapping.Code)
            ?? throw new InvalidOperationException("The column mapping does not name a Code column.");
        var name = ColumnIndexResolver.Resolve(headers, mapping.Name)
            ?? throw new InvalidOperationException("The column mapping does not name a Name column.");
        var unit = ColumnIndexResolver.Resolve(headers, mapping.Unit)
            ?? throw new InvalidOperationException("The column mapping does not name a Unit column.");
        var taxClass = ColumnIndexResolver.Resolve(headers, mapping.TaxClass)
            ?? throw new InvalidOperationException("The column mapping does not name a Tax Class column.");
        var price = ColumnIndexResolver.Resolve(headers, mapping.Price)
            ?? throw new InvalidOperationException("The column mapping does not name a Price column.");

        return new ResolvedColumns(
            code,
            name,
            ColumnIndexResolver.Resolve(headers, mapping.NameAlt),
            ColumnIndexResolver.Resolve(headers, mapping.Category),
            ColumnIndexResolver.Resolve(headers, mapping.Brand),
            unit,
            ColumnIndexResolver.Resolve(headers, mapping.Type),
            taxClass,
            ColumnIndexResolver.Resolve(headers, mapping.Location),
            ColumnIndexResolver.Resolve(headers, mapping.NonReturnable),
            ColumnIndexResolver.Resolve(headers, mapping.WarrantyDays),
            ColumnIndexResolver.Resolve(headers, mapping.Notes),
            ColumnIndexResolver.Resolve(headers, mapping.Barcode),
            price,
            ColumnIndexResolver.Resolve(headers, mapping.Cost),
            ColumnIndexResolver.Resolve(headers, mapping.Qty));
    }

    private static bool TryParseProductType(string text, out ProductType type, out string? error)
    {
        var normalised = text.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        switch (normalised)
        {
            case "STANDARD":
                type = ProductType.Standard;
                error = null;
                return true;
            case "DECIMAL":
            case "FRACTIONAL":
                type = ProductType.Fractional;
                error = null;
                return true;
            case "SERVICE":
                type = ProductType.Service;
                error = null;
                return true;
            case "NONINVENTORY":
                type = ProductType.NonInventory;
                error = null;
                return true;
            default:
                type = ProductType.Standard;
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{text}' is not a product type. Use Standard, Decimal, Service or Non-Inventory.");
                return false;
        }
    }

    private static bool TryParseNonNegativeMoney(string text, out Money value, out string? error)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            value = Money.Zero;
            error = string.Create(CultureInfo.InvariantCulture, $"'{text}' is not a number.");
            return false;
        }

        if (amount < 0m)
        {
            value = Money.Zero;
            error = string.Create(CultureInfo.InvariantCulture, $"'{text}' cannot be negative.");
            return false;
        }

        value = Money.FromDecimal(amount);
        error = null;
        return true;
    }

    private static bool ParseBoolish(string? text) =>
        text is not null
        && (string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "YES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase)
            || text == "1");

    private static ImportCounts Count(IReadOnlyList<RowPlan> plans) => new(
        plans.Count,
        plans.Count(plan => plan.Outcome == ImportRowOutcome.Create),
        plans.Count(plan => plan.Outcome == ImportRowOutcome.Update),
        plans.Count(plan => plan.Outcome == ImportRowOutcome.Skip),
        plans.Count(plan => plan.Outcome == ImportRowOutcome.Error));

    private static ImportPreviewReport BuildPreview(IReadOnlyList<RowPlan> plans) => new(
        Count(plans),
        [.. plans.Where(plan => plan.Outcome == ImportRowOutcome.Create).Take(SampleSize).Select(plan => plan.ToResult())],
        [.. plans.Where(plan => plan.Outcome == ImportRowOutcome.Update).Take(SampleSize).Select(plan => plan.ToResult())],
        [.. plans.Where(plan => plan.Outcome == ImportRowOutcome.Skip).Take(SampleSize).Select(plan => plan.ToResult())],
        [.. plans.Where(plan => plan.Outcome == ImportRowOutcome.Error).Take(SampleSize).Select(plan => plan.ToResult())]);

    private AuthenticatedUser RequireActor() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Catalogue import ran without a session. The role decorator should have refused this "
            + "call; the service is registered without it.");

    /// <summary>The column indices <see cref="ImportColumnMapping"/> resolves to for one file's headers.</summary>
    private sealed record ResolvedColumns(
        int Code,
        int Name,
        int? NameAlt,
        int? Category,
        int? Brand,
        int Unit,
        int? Type,
        int TaxClass,
        int? Location,
        int? NonReturnable,
        int? WarrantyDays,
        int? Notes,
        int? Barcode,
        int Price,
        int? Cost,
        int? Qty);

    /// <summary>
    /// One row, parsed and checked, everything <see cref="CommitAsync"/> needs to write it -
    /// built once by <see cref="ParseRowAsync"/> and shared, unchanged, between a dry run and a
    /// commit.
    /// </summary>
    private sealed class RowPlan
    {
        public required int RowNumber { get; init; }

        public required bool IsBlank { get; init; }

        public string? Code { get; init; }

        public string? Name { get; init; }

        public string? NameAlt { get; init; }

        public string? CategoryName { get; init; }

        public long? ResolvedCategoryId { get; init; }

        public string? BrandName { get; init; }

        public long? ResolvedBrandId { get; init; }

        public long? ResolvedUomId { get; init; }

        public ProductType ProductType { get; init; }

        public long? ResolvedTaxClassId { get; init; }

        public string? Location { get; init; }

        public bool NonReturnable { get; init; }

        public int? WarrantyDays { get; init; }

        public string? Notes { get; init; }

        public string? Barcode { get; init; }

        public Money Price { get; init; }

        public Money Cost { get; init; }

        public Quantity? TargetQty { get; init; }

        public long? ExistingProductId { get; init; }

        public long? ExistingVariantId { get; init; }

        public string? ExistingVariantSku { get; init; }

        public IReadOnlyDictionary<string, string>? ExistingVariantAttributes { get; init; }

        public long? ExistingBaseUomId { get; init; }

        public Percentage? ExistingMaxDiscountRate { get; init; }

        public required List<string> Errors { get; init; }

        public ImportRowOutcome Outcome =>
            IsBlank ? ImportRowOutcome.Skip
            : Errors.Count > 0 ? ImportRowOutcome.Error
            : ExistingProductId is null ? ImportRowOutcome.Create
            : ImportRowOutcome.Update;

        public static RowPlan BlankRow(int rowNumber) => new()
        {
            RowNumber = rowNumber,
            IsBlank = true,
            Errors = [],
        };

        public ImportRowResult ToResult() => new(RowNumber, Code, Outcome, [.. Errors]);
    }
}

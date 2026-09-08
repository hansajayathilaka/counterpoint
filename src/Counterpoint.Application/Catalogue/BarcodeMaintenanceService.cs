using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// The owner's barcode administration: multiple codes per variant, one primary, and the internal
/// barcode generator for loose or unbarcoded items (docs/01_DATA_MODEL.md §3, SRS FR-2.9,
/// FR-2.10, FR-2.24).
/// </summary>
/// <remarks>
/// Internal, for the same reason every other catalogue maintenance service is: the role check on
/// <see cref="IBarcodeMaintenance"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </remarks>
internal sealed class BarcodeMaintenanceService : IBarcodeMaintenance
{
    /// <summary>
    /// EAN/UPC readers treat the 20-29 prefix range as reserved for in-store use, which is the
    /// convention this default follows even though these codes are never checked against an
    /// outside registry - a familiar shape costs nothing and helps whoever is standing at the
    /// till recognise an internal code on sight.
    /// </summary>
    private const string DefaultInternalBarcodePrefix = "20";

    /// <summary>
    /// Not in <c>SettingKeys</c>: that class is deliberately scoped to the FR-10.1-10.8 settings
    /// <c>SettingsSerializer</c> flattens a whole <c>SettingsSnapshot</c> from and to (its own
    /// docs, and <c>SettingDefaultsTests.FR_10_1_to_10_8_EveryKeyIsPersistedExactlyOnce</c>
    /// enforces the two agree on every key, one for one). An internal barcode is not a document
    /// series or a receipt field, so this key is read and written directly through
    /// <see cref="ISettingStore"/> instead - the same pattern <c>SecurityPolicyRecorder</c> uses
    /// for its own <c>security.*</c> keys, and for the same reason: it is still the one
    /// <c>app_setting</c> table, still "reading never throws, falls back to a conservative
    /// default", and still audited on write, just through a narrower door than the eight-group
    /// framework.
    /// </summary>
    private const string InternalBarcodePrefixSettingKey = "catalogue.internal_barcode.prefix";

    private readonly IBarcodeStore _barcodes;
    private readonly IProductStore _products;
    private readonly IBarcodeSerialAllocator _serials;
    private readonly ISettingStore _settings;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public BarcodeMaintenanceService(
        IBarcodeStore barcodes,
        IProductStore products,
        IBarcodeSerialAllocator serials,
        ISettingStore settings,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(barcodes);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(serials);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _barcodes = barcodes;
        _products = products;
        _serials = serials;
        _settings = settings;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BarcodeRecord>> ListForVariantAsync(long variantId, CancellationToken cancellationToken = default) =>
        _barcodes.ListForVariantAsync(variantId, cancellationToken);

    /// <inheritdoc />
    public async Task<long> AddAsync(long variantId, string barcode, bool makePrimary, CancellationToken cancellationToken = default)
    {
        var trimmed = RequireBarcodeText(barcode);
        await RequireVariantAsync(variantId, cancellationToken).ConfigureAwait(false);
        await RequireNoConflictAsync(trimmed, cancellationToken).ConfigureAwait(false);

        return await AddCoreAsync(variantId, trimmed, makePrimary, generated: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GenerateInternalAsync(long variantId, bool makePrimary, CancellationToken cancellationToken = default)
    {
        await RequireVariantAsync(variantId, cancellationToken).ConfigureAwait(false);
        var prefix = await GetInternalBarcodePrefixAsync(cancellationToken).ConfigureAwait(false);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var serial = await _serials.AllocateAsync(token).ConfigureAwait(false);
                var barcode = InternalBarcodeGenerator.Generate(prefix, serial);

                await AddCoreAsync(variantId, barcode, makePrimary, generated: true, token).ConfigureAwait(false);
                return barcode;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPrimaryAsync(long barcodeId, CancellationToken cancellationToken = default)
    {
        var existing = await RequireBarcodeAsync(barcodeId, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _barcodes.ClearPrimaryAsync(existing.ProductVariantId, token).ConfigureAwait(false);
                await _barcodes.SetPrimaryAsync(barcodeId, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    barcodeId,
                    now,
                    before: BarcodeJson(existing),
                    after: BarcodeJson(existing with { IsPrimary = true }),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(long barcodeId, CancellationToken cancellationToken = default)
    {
        var existing = await RequireBarcodeAsync(barcodeId, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _barcodes.RemoveAsync(barcodeId, token).ConfigureAwait(false);

                if (existing.IsPrimary)
                {
                    var remaining = await _barcodes.ListForVariantAsync(existing.ProductVariantId, token).ConfigureAwait(false);

                    if (remaining.Count > 0)
                    {
                        await _barcodes.SetPrimaryAsync(remaining[0].Id, token).ConfigureAwait(false);
                    }
                }

                await RecordAsync(
                    CatalogueAuditActions.Deleted,
                    barcodeId,
                    now,
                    before: BarcodeJson(existing),
                    after: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GetInternalBarcodePrefixAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _settings.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        // Reading never throws (the settings framework's own rule): an unparseable or missing
        // row falls back to the conservative default rather than stopping barcode generation
        // (CLAUDE.md invariant 7).
        if (rows.TryGetValue(InternalBarcodePrefixSettingKey, out var stored)
            && stored.Value.Length is > 0 and <= 8
            && stored.Value.All(char.IsAsciiDigit))
        {
            return stored.Value;
        }

        return DefaultInternalBarcodePrefix;
    }

    /// <inheritdoc />
    public async Task SetInternalBarcodePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var trimmed = prefix.Trim();

        if (trimmed.Length is 0 or > 8 || !trimmed.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "An internal barcode prefix must be 1-8 digits, leaving room for a serial and a check digit.",
                nameof(prefix));
        }

        var before = await GetInternalBarcodePrefixAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(before, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        var now = _timeProvider.GetLocalNow();
        var userId = RequireActor();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _settings.WriteAsync(
                    [new SettingWrite(InternalBarcodePrefixSettingKey, trimmed, SettingValueTypes.Text, userId)],
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        userId,
                        SettingsAuditActions.SettingChanged,
                        SettingsAuditActions.SettingEntityType,
                        EntityId: null,
                        BeforeJson: SecurityAuditJson.Object(("value", before)),
                        AfterJson: SecurityAuditJson.Object(("value", trimmed))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> AddCoreAsync(
        long variantId,
        string barcode,
        bool makePrimary,
        bool generated,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var existing = await _barcodes.ListForVariantAsync(variantId, token).ConfigureAwait(false);

                // The first barcode a variant gets is always primary - there is no state in which
                // a variant has barcodes but none of them is the primary one (FR-2.9).
                var primary = makePrimary || existing.Count == 0;

                if (primary && existing.Count > 0)
                {
                    await _barcodes.ClearPrimaryAsync(variantId, token).ConfigureAwait(false);
                }

                var id = await _barcodes.AddAsync(variantId, barcode, primary, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created,
                    id,
                    now,
                    before: null,
                    after: SecurityAuditJson.Object(
                        ("product_variant_id", variantId),
                        ("barcode", barcode),
                        ("is_primary", primary),
                        ("generated", generated)),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireNoConflictAsync(string barcode, CancellationToken cancellationToken)
    {
        var conflict = await _barcodes.FindConflictAsync(barcode, cancellationToken).ConfigureAwait(false);

        if (conflict is not null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{barcode}' is already attached to '{conflict.ProductName}' (SKU {conflict.Sku}). A barcode belongs to exactly one item, so this cannot be added here as well (SRS FR-2.24)."));
        }
    }

    private async Task RequireVariantAsync(long variantId, CancellationToken cancellationToken)
    {
        _ = await _products.FindVariantByIdAsync(variantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no variant with id {variantId}. It may have been removed since this screen was opened."));
    }

    private async Task<BarcodeRecord> RequireBarcodeAsync(long barcodeId, CancellationToken cancellationToken) =>
        await _barcodes.FindByIdAsync(barcodeId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"There is no barcode with id {barcodeId}. It may have been removed since this screen was opened."));

    private static string RequireBarcodeText(string barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        var trimmed = barcode.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A barcode cannot be blank.");
        }

        return trimmed;
    }

    private static string BarcodeJson(BarcodeRecord record) => SecurityAuditJson.Object(
        ("product_variant_id", record.ProductVariantId),
        ("barcode", record.Value),
        ("is_primary", record.IsPrimary));

    private long RequireActor()
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Barcode maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return actor.Id;
    }

    private Task RecordAsync(
        string action,
        long barcodeId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            new AuditEntry(now, RequireActor(), action, CatalogueAuditActions.BarcodeEntityType, barcodeId, before, after),
            cancellationToken);
}

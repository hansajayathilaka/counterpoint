using System;
using System.Globalization;

namespace Counterpoint.Domain.Inventory;

/// <summary>What kind of scope a stock take was started against (P2-T10, SRS FR-4).</summary>
public enum StockTakeScopeKind
{
    /// <summary>Every active variant in the catalogue.</summary>
    All,

    /// <summary><c>product.category_id</c> matches exactly.</summary>
    Category,

    /// <summary><c>product.brand_id</c> matches exactly.</summary>
    Brand,

    /// <summary><c>product.location</c> (rack/bin) matches exactly.</summary>
    Location,
}

/// <summary>
/// Parses and renders <c>stock_take.scope</c> - free text of the shape <c>ALL</c>,
/// <c>CATEGORY:12</c>, <c>BRAND:5</c> or <c>LOCATION:A3</c> (docs/01_DATA_MODEL.md §4).
/// </summary>
/// <remarks>
/// Pure and framework-free, the same reason <c>PurchaseOrderStatuses</c> and
/// <c>Roles</c> are: what a scope token means is the shop's vocabulary, not a database concern,
/// so it is provable without a database and is not duplicated between the service that starts a
/// stock take and whatever, one day, renders it back on a screen.
/// </remarks>
public sealed record StockTakeScope(StockTakeScopeKind Kind, long? Id, string? Location)
{
    /// <summary>The token for <see cref="StockTakeScopeKind.All"/>.</summary>
    public const string AllToken = "ALL";

    private const string CategoryPrefix = "CATEGORY:";
    private const string BrandPrefix = "BRAND:";
    private const string LocationPrefix = "LOCATION:";

    /// <summary>
    /// Parses <paramref name="raw"/>. The keyword (<c>ALL</c>/<c>CATEGORY</c>/<c>BRAND</c>/
    /// <c>LOCATION</c>) is read case-insensitively - a shop keyer's capitalisation should not be
    /// able to fail a stock take - but the rendered token (<see cref="ToToken"/>) is always the
    /// canonical upper-case form the schema's own comment documents, so two scopes that mean the
    /// same thing are always stored identically.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="raw"/> is blank, names no recognised keyword, or a <c>CATEGORY</c>/
    /// <c>BRAND</c> id is not a positive integer.
    /// </exception>
    public static StockTakeScope Parse(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        var trimmed = raw.Trim();

        if (string.Equals(trimmed, AllToken, StringComparison.OrdinalIgnoreCase))
        {
            return new StockTakeScope(StockTakeScopeKind.All, null, null);
        }

        if (TryParseId(trimmed, CategoryPrefix, out var categoryId))
        {
            return new StockTakeScope(StockTakeScopeKind.Category, categoryId, null);
        }

        if (TryParseId(trimmed, BrandPrefix, out var brandId))
        {
            return new StockTakeScope(StockTakeScopeKind.Brand, brandId, null);
        }

        if (trimmed.StartsWith(LocationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var location = trimmed[LocationPrefix.Length..].Trim();
            if (location.Length == 0)
            {
                throw new ArgumentException(
                    "A LOCATION scope needs a rack or bin after the colon, for example 'LOCATION:A3'.",
                    nameof(raw));
            }

            return new StockTakeScope(StockTakeScopeKind.Location, null, location);
        }

        throw new ArgumentException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{raw}' is not a stock take scope. Use ALL, CATEGORY:<id>, BRAND:<id> or LOCATION:<rack>."),
            nameof(raw));
    }

    /// <summary>The canonical <c>stock_take.scope</c> token for this scope.</summary>
    public string ToToken() => Kind switch
    {
        StockTakeScopeKind.All => AllToken,
        StockTakeScopeKind.Category => CategoryPrefix + Id!.Value.ToString(CultureInfo.InvariantCulture),
        StockTakeScopeKind.Brand => BrandPrefix + Id!.Value.ToString(CultureInfo.InvariantCulture),
        StockTakeScopeKind.Location => LocationPrefix + Location,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "There are exactly four stock take scope kinds."),
    };

    private static bool TryParseId(string trimmed, string prefix, out long id)
    {
        id = 0;

        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idPart = trimmed[prefix.Length..].Trim();

        if (!long.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{trimmed}' needs a positive integer id after '{prefix}'."),
                nameof(trimmed));
        }

        return true;
    }
}

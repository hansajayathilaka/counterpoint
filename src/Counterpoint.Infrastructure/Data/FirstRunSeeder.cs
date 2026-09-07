using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Puts the minimum a till needs in order to ring up one bill into an empty database: a unit, a
/// tax class, one product with a variant and a barcode, an owner, an open shift, an opening
/// stock movement, and the <c>SALE</c> and <c>SHIFT</c> number sequences
/// (docs/01_DATA_MODEL.md §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the walking skeleton's stand-in for the first-run wizard, not the wizard.</b> The
/// real thing - shop profile, tax regime, printer, backup passphrase, and an owner account with
/// a password the owner chooses - is P1-T02 and P1-T03. In particular the account seeded here
/// carries a placeholder in <c>password_hash</c> that is not an Argon2id string, so no password
/// can ever verify against it. There is no default password, here or anywhere
/// (docs/01_DATA_MODEL.md §11).
/// </para>
/// <para>
/// <b>Idempotent, and one transaction.</b> Every row is guarded by its natural key, so running
/// it on every start adds nothing to a database that already has it, and a run that fails
/// half way leaves nothing behind at all.
/// </para>
/// </remarks>
public sealed class FirstRunSeeder
{
    /// <summary>The document type whose sequence a bill is numbered from.</summary>
    private const string SaleDocumentType = "SALE";

    /// <summary>The document type whose sequence a shift is numbered from.</summary>
    private const string ShiftDocumentType = "SHIFT";

    /// <summary>Q-16's documented default: <c>INV-2026-000001</c>.</summary>
    private const string SaleNumberPattern = "{prefix}{yyyy}-{n:000000}";

    /// <summary>Q-16's documented default: <c>SH-000001</c>. A shift is not numbered by year.</summary>
    private const string ShiftNumberPattern = "{prefix}{n:000000}";

    private const string SaleNumberPrefix = "INV-";
    private const string ShiftNumberPrefix = "SH-";
    private const string OwnerUsername = "owner";
    private const string ProductCode = "SKEL-001";
    private const string VariantSku = "SKEL-001-A";
    private const string OpenStatus = "OPEN";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> an opening count posts.</summary>
    private const string OpeningMovementType = "OPENING";

    /// <summary>
    /// Not an Argon2id encoded string, so nothing can authenticate as this account. The owner's
    /// real credentials are created in P1-T02.
    /// </summary>
    private const string UnusablePasswordHash = "!";

    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IStockLedger _stock;

    public FirstRunSeeder(
        SqliteUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IDocumentNumberAllocator numbers,
        IStockLedger stock)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(stock);

        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _numbers = numbers;
        _stock = stock;
    }

    /// <summary>The barcode printed on the seeded product's packet.</summary>
    public static string SeededBarcode => "5901234123457";

    /// <summary>
    /// Seeds anything that is missing. Safe on every start.
    /// </summary>
    /// <returns>True when it wrote something, false when the database already had it all.</returns>
    public Task<bool> EnsureSeededAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var now = _timeProvider.GetLocalNow();
                var wrote = false;

                wrote |= await SeedNumberSequenceAsync(context, token).ConfigureAwait(false);
                var uomId = await SeedUomAsync(context, token).ConfigureAwait(false);
                var taxClassId = await SeedTaxClassAsync(context, token).ConfigureAwait(false);
                var userId = await SeedOwnerAsync(context, now, token).ConfigureAwait(false);
                var variantId = await SeedProductAsync(context, uomId, taxClassId, now, token).ConfigureAwait(false);

                wrote |= await SeedBarcodeAsync(context, variantId, token).ConfigureAwait(false);
                wrote |= await SeedOpeningStockAsync(context, variantId, uomId, userId, now, token)
                    .ConfigureAwait(false);
                wrote |= await SeedShiftAsync(context, userId, now, token).ConfigureAwait(false);

                return wrote;
            },
            cancellationToken);

    /// <summary>
    /// Seeds every sequence a document number is drawn from on the skeleton's paths: the bill,
    /// and the shift the bill is rung up inside (docs/01_DATA_MODEL.md §11). Nothing may number
    /// a document any other way (CLAUDE.md invariant 4).
    /// </summary>
    private static async Task<bool> SeedNumberSequenceAsync(PosDbContext context, CancellationToken token)
    {
        var wrote = await SeedSequenceAsync(context, SaleDocumentType, SaleNumberPrefix, SaleNumberPattern, token)
            .ConfigureAwait(false);

        wrote |= await SeedSequenceAsync(context, ShiftDocumentType, ShiftNumberPrefix, ShiftNumberPattern, token)
            .ConfigureAwait(false);

        return wrote;
    }

    private static async Task<bool> SeedSequenceAsync(
        PosDbContext context,
        string docType,
        string prefix,
        string pattern,
        CancellationToken token)
    {
        if (await context.Set<NumberSequence>()
                .AnyAsync(row => row.DocType == docType, token)
                .ConfigureAwait(false))
        {
            return false;
        }

        context.Add(new NumberSequence
        {
            DocType = docType,
            Prefix = prefix,
            Pattern = pattern,

            // The first document is number 1, and the allocator returns the value before the
            // increment - so this is 1, not 0 (CLAUDE.md invariant 4).
            NextVal = 1,
        });

        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return true;
    }

    private static async Task<long> SeedUomAsync(PosDbContext context, CancellationToken token)
    {
        var existing = await context.Set<Uom>()
            .Where(row => row.Name == "Piece")
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Value;
        }

        var row = new Uom { Name = "Piece", Symbol = "pc", DecimalPlaces = 0 };
        context.Add(row);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        return row.Id;
    }

    private static async Task<long> SeedTaxClassAsync(PosDbContext context, CancellationToken token)
    {
        var existing = await context.Set<TaxClass>()
            .Where(row => row.Name == "Zero rated")
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Value;
        }

        // Zero rated until Q-02 answers what the shop actually charges (P1-T03). A rate the
        // skeleton invented would be a wrong number printed on a bill, which is worse than none.
        var row = new TaxClass { Name = "Zero rated", Rate = TaxRate.Zero, Active = true };
        context.Add(row);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        return row.Id;
    }

    private static async Task<long> SeedOwnerAsync(
        PosDbContext context,
        DateTimeOffset now,
        CancellationToken token)
    {
        var existing = await context.Set<AppUser>()
            .Where(row => row.Username == OwnerUsername)
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Value;
        }

        var row = new AppUser
        {
            Username = OwnerUsername,
            DisplayName = "Shop Owner",
            PasswordHash = UnusablePasswordHash,
            Role = "OWNER",
            Active = true,
            FailedAttempts = 0,
            LockedUntil = null,
            LastLogin = null,
            CreatedAt = now,
        };

        context.Add(row);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        return row.Id;
    }

    private static async Task<long> SeedProductAsync(
        PosDbContext context,
        long uomId,
        long taxClassId,
        DateTimeOffset now,
        CancellationToken token)
    {
        var existingVariant = await context.Set<ProductVariant>()
            .Where(row => row.Sku == VariantSku)
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (existingVariant is not null)
        {
            return existingVariant.Value;
        }

        var product = new Product
        {
            Code = ProductCode,
            Name = "Galvanised bolt M8",
            NameAlt = null,
            CategoryId = null,
            BrandId = null,
            BaseUomId = uomId,
            Type = "STANDARD",
            TaxClassId = taxClassId,
            CostAvg = Money.FromDecimal(9.00m),
            ReorderLevel = 0,
            ReorderQty = 0,
            Location = "A3",
            NonReturnable = false,
            MinSellQty = 0,
            MaxDiscountRate = null,
            WarrantyDays = null,
            Notes = null,
            ImagePath = null,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(product);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = VariantSku,
            Attributes = """{"size":"M8"}""",
            Price = Money.FromDecimal(12.50m),
            Active = true,
            CreatedAt = now,
        };

        context.Add(variant);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        return variant.Id;
    }

    private static async Task<bool> SeedBarcodeAsync(PosDbContext context, long variantId, CancellationToken token)
    {
        var barcode = SeededBarcode;

        if (await context.Set<Barcode>().AnyAsync(row => row.Value == barcode, token).ConfigureAwait(false))
        {
            return false;
        }

        context.Add(new Barcode
        {
            ProductVariantId = variantId,
            Value = barcode,
            IsPrimary = true,
        });

        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Posts the opening count through the ledger, never straight into the projection.
    /// </summary>
    /// <remarks>
    /// The balance table is a projection and has to be rebuildable from <c>stock_movement</c>
    /// (CLAUDE.md invariant 3). A balance written directly would be a number the ledger cannot
    /// account for, and a rebuild - P1-T07's <c>RebuildStockBalanceCommand</c> - would silently
    /// erase the shop's opening stock. So the opening count is an <c>OPENING</c> movement like
    /// any other, and <see cref="IStockLedger"/> creates the projection row behind it.
    /// </remarks>
    private async Task<bool> SeedOpeningStockAsync(
        PosDbContext context,
        long variantId,
        long uomId,
        long userId,
        DateTimeOffset now,
        CancellationToken token)
    {
        if (await context.Set<StockBalance>()
                .AnyAsync(row => row.ProductVariantId == variantId, token)
                .ConfigureAwait(false))
        {
            return false;
        }

        await _stock.PostAsync(
            new StockPosting(
                variantId,
                OpeningMovementType,
                Quantity.FromDecimal(100m, uomId),
                Money.FromDecimal(9.00m),
                OpeningMovementType,

                // An opening count answers to no document. ref_doc_id is nullable for exactly
                // this case (docs/01_DATA_MODEL.md, stock_movement).
                RefDocId: null,
                userId,
                now,
                Note: "Opening count seeded on first run."),
            token).ConfigureAwait(false);

        return true;
    }

    private async Task<bool> SeedShiftAsync(
        PosDbContext context,
        long userId,
        DateTimeOffset now,
        CancellationToken token)
    {
        // C-01: the database permits exactly one open shift, so this both guards the seed and
        // avoids opening a second one over a shift somebody is already trading in.
        if (await context.Set<Shift>().AnyAsync(row => row.Status == OpenStatus, token).ConfigureAwait(false))
        {
            return false;
        }

        // From number_sequence, in this seeding transaction, exactly as a bill draws its
        // bill_no. Counting the rows would be MAX(n)+1 by another name, and the first shift the
        // sequence later issues would collide with it on shift_no UNIQUE
        // (CLAUDE.md invariant 4).
        var shiftNo = await _numbers
            .AllocateAsync(ShiftDocumentType, DateOnly.FromDateTime(now.Date), token)
            .ConfigureAwait(false);

        context.Add(new Shift
        {
            ShiftNo = shiftNo,
            UserId = userId,
            OpenedAt = now,
            BusinessDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            OpeningFloat = Money.Zero,
            ClosedAt = null,
            CountedCash = null,
            ExpectedCash = null,
            Variance = null,
            Status = OpenStatus,
            ClosedBy = null,
            Note = null,
        });

        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return true;
    }
}

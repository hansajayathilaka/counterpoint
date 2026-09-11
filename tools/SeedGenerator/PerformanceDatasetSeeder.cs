using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.SeedGenerator;

/// <summary>
/// Builds the "aged shop" database <c>P1-T16</c>'s performance harness and AC-18 both measure
/// against: <c>skuCount</c> SKUs and a spread of historical bills totalling
/// <c>totalBillLines</c> sale lines, over the trailing <see cref="HistoricalDayCount"/> business
/// days.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists separately from <see cref="FirstRunSeeder"/>.</b> The skeleton seeds one
/// product and no history at all - exactly what a brand-new till looks like. NFR-P1...P4's real
/// risk is not a brand-new till; it is the till three years into trading, with an index that
/// degrades as <c>sale</c>, <c>sale_line</c> and <c>stock_movement</c> grow
/// (docs/03_PHASE_1_core_trading.md P1-T16 "Risks"). This class is what makes that database
/// exist so the harness can measure the real risk instead of an empty one.
/// </para>
/// <para>
/// <b>Raw batched SQL, not <see cref="Counterpoint.Application.Sales.ICompleteSale"/>, on
/// purpose, and why this lives in a tool project rather than in
/// <c>Counterpoint.Infrastructure</c>.</b> Same reasoning as
/// <c>tests/Counterpoint.Integration.Tests/Data/TradingDaySeed.cs</c> and
/// <c>tests/Counterpoint.Integration.Tests/Catalogue/CataloguePerformanceTests.cs</c>: this is
/// tooling data generation, not a business transaction, and going through the real handler one
/// bill at a time would spend most of a run on tens of thousands of individual <c>fsync</c>s
/// (<c>synchronous=FULL</c>, CLAUDE.md invariant 9) rather than on what is actually being
/// measured. It writes <c>stock_balance</c> directly and does not call <c>StockLedger.PostAsync</c>
/// - CLAUDE.md invariant 3 ("every stock change goes through StockLedger.PostAsync") is about
/// what a live till does while trading; this is a one-time synthetic dataset a developer or CI
/// job generates offline, the same exception the two test-tree fixtures above already take. It
/// is kept out of <c>Counterpoint.Infrastructure</c> - which <em>is</em> shipped - precisely so
/// that exception can never reach the product; <c>Counterpoint.App</c> does not and must not
/// reference this project (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// What still has to be got right by hand, because the harness measures against it and AC-15's
/// process-kill test verifies it, is the hash chain (CLAUDE.md invariant 6): every seeded
/// <c>sale</c> and <c>audit_log</c> row is chained exactly as <c>SaleHashChain</c> and
/// <c>AuditLogHashChain</c> define it, from <c>HashChain.GenesisHash</c> forward - reusing those
/// internal classes (InternalsVisibleTo, Counterpoint.Infrastructure.csproj) rather than
/// reimplementing the format a second time - and <c>number_sequence.next_val</c> is left exactly
/// where a real till would have left it, drawn from the same row a live bill's
/// <c>UPDATE ... RETURNING</c> reads, just advanced by hand here to match the rows already
/// written under the same <c>doc_type</c> - never a second, independent numbering scheme. A bill
/// completed afterwards through the real handler continues the same sequence and the same chain
/// without any special case.
/// </para>
/// <para>
/// Must run after migrations and <see cref="FirstRunSeeder.EnsureSeededAsync"/>: this class reads
/// the seeded "Piece" unit and "Exempt" tax class rather than creating its own, and leaves the
/// skeleton's own product, shift and number-sequence rows untouched.
/// </para>
/// </remarks>
internal static class PerformanceDatasetSeeder
{
    /// <summary>Trailing business days the historical bills are spread across.</summary>
    internal const int HistoricalDayCount = 60;

    private const int SeedBatchSize = 2_000;

    private const long ProductIdBase = 1_000_000;
    private const long VariantIdBase = 2_000_000;
    private const long BarcodeIdBase = 3_000_000;
    private const long ProductUomIdBase = 4_000_000;

    private const long ShiftIdBase = 10_000_000;
    private const long SaleIdBase = 20_000_000;
    /// <summary>The one shift <see cref="FirstRunSeeder"/> opens (docs/01_DATA_MODEL.md §11's skeleton).</summary>
    private const long SkeletonShiftId = 1;
    /// <summary>The skeleton's own shift number, "SH-000001" - the historical days continue from here.</summary>
    private const long SkeletonShiftSequenceNumber = 1;
    private const long SaleLineIdBase = 30_000_000;
    private const long PaymentIdBase = 40_000_000;
    private const long StockMovementIdBase = 50_000_000;
    private const long AuditLogIdBase = 60_000_000;

    private const long BaseConversionFactor = 10_000; // UomConversion.Base, scaled x10000.

    /// <summary>Opening stock per SKU, generous enough that the historical sales never force the
    /// default "allow negative" policy (Q-11) to actually go negative for a typical seed.</summary>
    private const long OpeningQtyBase = 1_000_0000; // 1,000 pieces, scaled x10000.

    /// <summary>Seeds the catalogue and the historical bills, in one transaction.</summary>
    /// <param name="unitOfWork">The write gate. Runs the whole seed as one transaction.</param>
    /// <param name="clock">Where "today" and the historical business dates count back from.</param>
    /// <param name="skuCount">How many products/variants to seed.</param>
    /// <param name="totalBillLines">
    /// How many historical <c>sale_line</c> rows to seed, spread over
    /// <see cref="HistoricalDayCount"/> days and a varying number of lines per bill (a realistic
    /// spread, not one line per bill).
    /// </param>
    internal static Task SeedAsync(
        SqliteUnitOfWork unitOfWork,
        TimeProvider clock,
        int skuCount,
        int totalBillLines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(skuCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(totalBillLines, 0);

        return unitOfWork.ExecuteInTransactionAsync<object?>(async (connection, transaction, token) =>
        {
            // sale_line and stock_movement are flushed to disk in their own 2,000-row batches,
            // independently of the sale/payment/audit_log batches they belong to (the whole point
            // of batching bulk inserts) - so a sale_line row can legitimately hit the wire before
            // the sale row it references does. foreign_keys stays ON (CLAUDE.md invariant 9);
            // this only moves the check from per-statement to COMMIT, which is what lets rows
            // land out of referential order within one all-or-nothing transaction.
            await ExecuteAsync(connection, transaction, "PRAGMA defer_foreign_keys = ON;", token)
                .ConfigureAwait(false);

            var (baseUomId, taxClassId) = await ReferenceIdsAsync(connection, transaction, token)
                .ConfigureAwait(false);

            var prices = await SeedCatalogueAsync(
                connection, transaction, skuCount, baseUomId, taxClassId, token).ConfigureAwait(false);

            var today = DateOnly.FromDateTime(clock.GetLocalNow().Date);

            // Only one shift may be OPEN at a time (the partial unique index C-01 relies on), and
            // FirstRunSeeder already holds that slot with the skeleton's shift (id 1). It has no
            // sales against it, so closing it early - the same column-scoped UPDATE a real shift
            // close performs (CLAUDE.md invariant 5, trg_shift_close_fields_together) - simply
            // makes it an empty, already-closed "day zero" and frees the slot for the first
            // historical day below.
            await CloseShiftAsync(connection, transaction, SkeletonShiftId, today, subtotal: 0, token)
                .ConfigureAwait(false);

            await SeedHistoricalBillsAsync(
                connection, transaction, skuCount, totalBillLines, prices, baseUomId, today, token)
                .ConfigureAwait(false);

            return null;
        }, cancellationToken);
    }

    private static async Task<(long BaseUomId, long TaxClassId)> ReferenceIdsAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        var baseUomId = await ScalarAsync(connection, transaction, "SELECT id FROM uom WHERE name = 'Piece';", token)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "No 'Piece' unit found. FirstRunSeeder must run before PerformanceDatasetSeeder.");

        var taxClassId = await ScalarAsync(connection, transaction, "SELECT id FROM tax_class WHERE name = 'Exempt';", token)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "No 'Exempt' tax class found. FirstRunSeeder must run before PerformanceDatasetSeeder.");

        return (baseUomId, taxClassId);
    }

    /// <summary>
    /// <c>skuCount</c> products, one variant each, a base <c>product_uom</c> row, a primary
    /// barcode and an opening <c>stock_balance</c>. Returns each variant's selling price (scaled),
    /// keyed by its index, so the historical bills below price lines against the same catalogue.
    /// </summary>
    private static async Task<long[]> SeedCatalogueAsync(
        DbConnection connection,
        DbTransaction transaction,
        int skuCount,
        long baseUomId,
        long taxClassId,
        CancellationToken token)
    {
        const string timestamp = "2020-01-01T08:00:00.000+05:30";
        var prices = new long[skuCount];

        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, location, created_at, updated_at) VALUES ",
            skuCount, SeedBatchSize,
            i =>
            {
                var id = ProductIdBase + i;
                var code = "PERF-" + i.ToString("000000", CultureInfo.InvariantCulture);
                var name = "Hardware Item " + i.ToString("000000", CultureInfo.InvariantCulture);
                var location = "A" + (i % 40 + 1).ToString(CultureInfo.InvariantCulture);
                return $"({id},'{code}','{name}',{baseUomId},'STANDARD',{taxClassId},'{location}','{timestamp}','{timestamp}')";
            },
            token).ConfigureAwait(false);

        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO product_variant (id, product_id, sku, price, active, created_at) VALUES ",
            skuCount, SeedBatchSize,
            i =>
            {
                var id = VariantIdBase + i;
                var productId = ProductIdBase + i;
                var sku = "PERF-" + i.ToString("000000", CultureInfo.InvariantCulture) + "-A";

                // A spread from 1.00 to ~500.00, scaled x10000 (CLAUDE.md invariant 1) - not a
                // flat catalogue.
                var price = 10_000L + (i % 500) * 10_000L;
                prices[i] = price;

                return $"({id},{productId},'{sku}',{price},1,'{timestamp}')";
            },
            token).ConfigureAwait(false);

        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO product_uom (id, product_id, uom_id, conversion_factor, is_base) VALUES ",
            skuCount, SeedBatchSize,
            i => $"({ProductUomIdBase + i},{ProductIdBase + i},{baseUomId},{BaseConversionFactor},1)",
            token).ConfigureAwait(false);

        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO barcode (id, product_variant_id, barcode, is_primary) VALUES ",
            skuCount, SeedBatchSize,
            i => $"({BarcodeIdBase + i},{VariantIdBase + i},'{BarcodeFor(i)}',1)",
            token).ConfigureAwait(false);

        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO stock_balance (product_variant_id, qty_base, cost_avg, updated_at) VALUES ",
            skuCount, SeedBatchSize,
            i => $"({VariantIdBase + i},{OpeningQtyBase},{prices[i] * 7L / 10L},'{timestamp}')",
            token).ConfigureAwait(false);

        // stock_balance is a projection; it must be rebuildable from stock_movement (CLAUDE.md
        // invariant 3). Every seeded variant's opening quantity above is backed here by a real
        // OPENING movement - ref_doc_type/ref_doc_id following FirstRunSeeder.SeedOpeningStockAsync's
        // convention (an opening count answers to no document, so ref_doc_id is NULL) - so
        // RebuildStockBalanceCommand/SqliteStockConsistencyCheck find a ledger that actually
        // accounts for the balance, not a number the ledger has never heard of. One movement id per
        // SKU, reserved below StockMovementIdBase's SALE range (see SeedHistoricalBillsAsync).
        await ExecuteBatchedAsync(
            connection, transaction,
            "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, "
            + "ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at, note) VALUES ",
            skuCount, SeedBatchSize,
            i => $"({StockMovementIdBase + i},{VariantIdBase + i},'OPENING',{OpeningQtyBase},{prices[i] * 7L / 10L},"
                + $"'OPENING',NULL,{OpeningQtyBase},1,'{timestamp}',NULL)",
            token).ConfigureAwait(false);

        return prices;
    }

    /// <summary>A 13-digit numeric code unique across the seeded range, EAN-13 shaped but not check-digit valid.</summary>
    /// <summary>
    /// The barcode a seeded variant carries, by its zero-based index - <c>internal</c> (not
    /// <c>private</c>) so the performance regression guard
    /// (tests/Counterpoint.Integration.Tests/Performance) can scan a known SKU without
    /// re-deriving this format independently and risking the two falling out of step.
    /// </summary>
    internal static string BarcodeFor(int index) => "8" + index.ToString("000000000000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Closes one shift with the exact set of columns
    /// <c>trg_shift_close_fields_together</c> requires change together, and only those - the same
    /// column-scoped update a real Z report performs (CLAUDE.md invariant 5).
    /// </summary>
    private static Task CloseShiftAsync(
        DbConnection connection, DbTransaction transaction, long shiftId, DateOnly closedOn, long subtotal, CancellationToken token)
    {
        var closedAt = closedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T18:00:00.000+05:30";
        var cash = 5_000_000L + subtotal;

        return ExecuteAsync(
            connection, transaction,
            "UPDATE shift SET status = 'CLOSED', closed_at = $closed_at, counted_cash = $cash, "
            + "expected_cash = $cash, variance = 0, closed_by = 1, note = NULL WHERE id = $id;",
            token,
            ("$closed_at", closedAt),
            ("$cash", cash),
            ("$id", shiftId));
    }

    /// <summary>
    /// The historical bills themselves, one calendar day at a time: <paramref name="totalBillLines"/>
    /// <c>sale_line</c> rows spread over a varying number of lines per bill, across
    /// <see cref="HistoricalDayCount"/> days, each bill hash-chained to the one before it (CLAUDE.md
    /// invariant 6) and each referencing a real, already-seeded <c>product_variant</c>.
    /// </summary>
    /// <remarks>
    /// Each day gets its own shift, opened, sold into, and closed before the next day's shift
    /// opens - never two shifts open together (the partial unique index C-01 relies on) and never
    /// a sale posted against an already-closed one (<c>trg_shift_no_post_into_closed</c>-style
    /// guard the schema enforces). A final shift is opened after the last historical day and left
    /// OPEN, so the database this method leaves behind is exactly what a real till looks like the
    /// morning after: history behind it, one shift open in front of it.
    /// </remarks>
    private static async Task SeedHistoricalBillsAsync(
        DbConnection connection,
        DbTransaction transaction,
        int skuCount,
        int totalBillLines,
        long[] prices,
        long baseUomId,
        DateOnly today,
        CancellationToken token)
    {
        const long UserId = 1;
        var random = new Random(20260910);

        var saleBatch = new List<string>(SeedBatchSize);
        var lineBatch = new List<string>(SeedBatchSize);
        var paymentBatch = new List<string>(SeedBatchSize);
        var movementBatch = new List<string>(SeedBatchSize);
        var auditBatch = new List<string>(SeedBatchSize);

        var prevSaleHash = HashChain.GenesisHash;
        var prevAuditHash = HashChain.GenesisHash;

        var saleId = SaleIdBase;
        var lineId = SaleLineIdBase;
        var paymentId = PaymentIdBase;

        // The OPENING row SeedCatalogueAsync writes per SKU claims StockMovementIdBase ..
        // StockMovementIdBase + skuCount - 1; the SALE movements below start one past that range so
        // no id collides with it.
        var movementId = StockMovementIdBase + skuCount;
        var auditId = AuditLogIdBase;

        var billNo = 0L;
        var shiftSequenceNumber = SkeletonShiftSequenceNumber;

        // The real running on-hand balance per variant, seeded at OpeningQtyBase (the same
        // quantity the OPENING movement above recorded) and decremented as each SALE movement below
        // is generated, in the same chronological order it is applied - so balance_after is a true
        // fact per row (docs/01_DATA_MODEL.md), not the placeholder 0 this used to write.
        var runningBalance = new long[skuCount];
        Array.Fill(runningBalance, OpeningQtyBase);

        // An even share of the lines per day, the remainder folded into the last day - not
        // perfectly flat (NextVariantIndex already gives the catalogue a popularity skew), just
        // spread across real business days rather than one giant day.
        var linesPerDay = totalBillLines / HistoricalDayCount;

        for (var dayIndex = 0; dayIndex < HistoricalDayCount; dayIndex++)
        {
            var day = today.AddDays(-(HistoricalDayCount - dayIndex));
            var shiftId = ShiftIdBase + dayIndex;
            shiftSequenceNumber++;

            var openedAt = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T08:00:00.000+05:30";
            var shiftNo = "SH-" + shiftSequenceNumber.ToString("000000", CultureInfo.InvariantCulture);

            await ExecuteAsync(
                connection, transaction,
                "INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float, "
                + "closed_at, counted_cash, expected_cash, variance, status, closed_by, note) VALUES "
                + $"({shiftId},'{shiftNo}',{UserId},'{openedAt}','{day:yyyy-MM-dd}',5000000,"
                + "NULL,NULL,NULL,NULL,'OPEN',NULL,NULL);",
                token).ConfigureAwait(false);

            var linesToday = dayIndex == HistoricalDayCount - 1
                ? totalBillLines - (linesPerDay * (HistoricalDayCount - 1))
                : linesPerDay;

            var daySubtotal = 0L;

            while (linesToday > 0)
            {
                var soldAt = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "T" + (9 + random.Next(0, 9)).ToString("00", CultureInfo.InvariantCulture)
                    + ":" + random.Next(0, 60).ToString("00", CultureInfo.InvariantCulture)
                    + ":00.000+05:30";

                var linesOnThisBill = Math.Min(linesToday, 1 + random.Next(0, 6));
                linesToday -= linesOnThisBill;
                billNo++;

                var subtotal = 0L;
                var cogs = 0L;

                for (var line = 0; line < linesOnThisBill; line++)
                {
                    var variantIndex = NextVariantIndex(random, skuCount);
                    var unitPrice = prices[variantIndex];
                    var qtyUnits = 1 + random.Next(0, 5);
                    var qtyBase = qtyUnits * 10_000L;
                    var lineTotal = unitPrice * qtyUnits;
                    var unitCost = unitPrice * 7L / 10L;

                    subtotal += lineTotal;
                    cogs += unitCost * qtyUnits;

                    lineBatch.Add(
                        $"({lineId},{saleId},{line + 1},{VariantIdBase + variantIndex},'Hardware Item {variantIndex:000000}',"
                        + $"{qtyBase},{baseUomId},{qtyBase},{unitPrice},0,0,0,{lineTotal},{unitCost},0,NULL)");

                    runningBalance[variantIndex] -= qtyBase;

                    movementBatch.Add(
                        $"({movementId},{VariantIdBase + variantIndex},'SALE',{-qtyBase},{unitCost},"
                        + $"'SALE',{saleId},{runningBalance[variantIndex]},{UserId},'{soldAt}',NULL)");

                    lineId++;
                    movementId++;

                    if (lineBatch.Count >= SeedBatchSize)
                    {
                        await FlushAsync(connection, transaction,
                            "INSERT INTO sale_line (id, sale_id, line_no, product_variant_id, description, qty, "
                            + "uom_id, qty_base, unit_price, discount, tax_rate, tax, line_total, unit_cost, "
                            + "qty_returned, note) VALUES ", lineBatch, token).ConfigureAwait(false);
                    }

                    if (movementBatch.Count >= SeedBatchSize)
                    {
                        await FlushAsync(connection, transaction,
                            "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, "
                            + "ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at, note) VALUES ",
                            movementBatch, token).ConfigureAwait(false);
                    }
                }

                daySubtotal += subtotal;

                var billNoText = "INV-" + day.Year.ToString("0000", CultureInfo.InvariantCulture)
                    + "-" + billNo.ToString("000000", CultureInfo.InvariantCulture);

                var sale = new Sale
                {
                    BillNo = billNoText,
                    SoldAt = DateTimeOffset.Parse(soldAt, CultureInfo.InvariantCulture),
                    BusinessDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CustomerId = null,
                    UserId = UserId,
                    ShiftId = shiftId,
                    Subtotal = Money.FromScaled(subtotal),
                    LineDiscount = Money.Zero,
                    BillDiscount = Money.Zero,
                    Tax = Money.Zero,
                    Rounding = Money.Zero,
                    Total = Money.FromScaled(subtotal),
                    Cogs = Money.FromScaled(cogs),
                    Status = "COMPLETED",
                    CancelledBy = null,
                    CancelledAt = null,
                    Note = null,
                };

                var rowHash = SaleHashChain.RowHash(prevSaleHash, sale);

                saleBatch.Add(
                    $"({saleId},'{billNoText}','{soldAt}','{day:yyyy-MM-dd}',NULL,{UserId},{shiftId},"
                    + $"{subtotal},0,0,0,0,{subtotal},{cogs},'COMPLETED',NULL,NULL,NULL,'{prevSaleHash}','{rowHash}')");

                paymentBatch.Add(
                    $"({paymentId},{saleId},NULL,'CASH',{subtotal},NULL,'{soldAt}')");

                var afterJson = string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"bill_no":"{{billNoText}}","total":{{subtotal}}}""");

                var auditEntry = new AuditLog
                {
                    OccurredAt = sale.SoldAt,
                    UserId = UserId,
                    Action = "SALE_COMPLETED",
                    EntityType = "sale",
                    EntityId = saleId,
                    BeforeJson = null,
                    AfterJson = afterJson,
                    Reason = null,
                };

                var auditHash = AuditLogHashChain.RowHash(prevAuditHash, auditEntry);

                auditBatch.Add(
                    $"({auditId},'{soldAt}',{UserId},'SALE_COMPLETED','sale',{saleId},NULL,"
                    + $"'{afterJson}',NULL,'{prevAuditHash}','{auditHash}')");

                prevSaleHash = rowHash;
                prevAuditHash = auditHash;

                saleId++;
                paymentId++;
                auditId++;

                if (saleBatch.Count >= SeedBatchSize)
                {
                    await FlushAsync(connection, transaction,
                        "INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id, "
                        + "subtotal, line_discount, bill_discount, tax, rounding, total, cogs, status, cancelled_by, "
                        + "cancelled_at, note, prev_hash, row_hash) VALUES ", saleBatch, token).ConfigureAwait(false);

                    await FlushAsync(connection, transaction,
                        "INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, reference, paid_at) VALUES ",
                        paymentBatch, token).ConfigureAwait(false);

                    await FlushAsync(connection, transaction,
                        "INSERT INTO audit_log (id, occurred_at, user_id, action, entity_type, entity_id, "
                        + "before_json, after_json, reason, prev_hash, row_hash) VALUES ",
                        auditBatch, token).ConfigureAwait(false);
                }
            }

            // Force-flushed here, every day, before the CLOSE update below: a sale row buffered
            // past its own shift's close would either violate the "no posting into a closed
            // shift" guard or (worse) silently land against the wrong day.
            await FlushAsync(connection, transaction,
                "INSERT INTO sale_line (id, sale_id, line_no, product_variant_id, description, qty, "
                + "uom_id, qty_base, unit_price, discount, tax_rate, tax, line_total, unit_cost, "
                + "qty_returned, note) VALUES ", lineBatch, token).ConfigureAwait(false);

            await FlushAsync(connection, transaction,
                "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, "
                + "ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at, note) VALUES ",
                movementBatch, token).ConfigureAwait(false);

            await FlushAsync(connection, transaction,
                "INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id, "
                + "subtotal, line_discount, bill_discount, tax, rounding, total, cogs, status, cancelled_by, "
                + "cancelled_at, note, prev_hash, row_hash) VALUES ", saleBatch, token).ConfigureAwait(false);

            await FlushAsync(connection, transaction,
                "INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, reference, paid_at) VALUES ",
                paymentBatch, token).ConfigureAwait(false);

            await FlushAsync(connection, transaction,
                "INSERT INTO audit_log (id, occurred_at, user_id, action, entity_type, entity_id, "
                + "before_json, after_json, reason, prev_hash, row_hash) VALUES ",
                auditBatch, token).ConfigureAwait(false);

            await CloseShiftAsync(connection, transaction, shiftId, day, daySubtotal, token).ConfigureAwait(false);
        }

        // The morning after the last historical day: one shift, open, exactly what a real till
        // looks like before its first live bill (SRS FR-8.1).
        shiftSequenceNumber++;
        var finalShiftId = ShiftIdBase + HistoricalDayCount;
        var finalOpenedAt = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T08:00:00.000+05:30";
        var finalShiftNo = "SH-" + shiftSequenceNumber.ToString("000000", CultureInfo.InvariantCulture);

        await ExecuteAsync(
            connection, transaction,
            "INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float, "
            + "closed_at, counted_cash, expected_cash, variance, status, closed_by, note) VALUES "
            + $"({finalShiftId},'{finalShiftNo}',{UserId},'{finalOpenedAt}','{today:yyyy-MM-dd}',5000000,"
            + "NULL,NULL,NULL,NULL,'OPEN',NULL,NULL);",
            token).ConfigureAwait(false);

        // Leaves both sequences exactly where a real till would: the next bill or shift opened
        // through the real handlers draws the number one past the last one this method wrote,
        // from the same number_sequence row a live "UPDATE ... RETURNING" reads (CLAUDE.md
        // invariant 4) - never a second, independent counter and never derived from the highest
        // number already written.
        await ExecuteAsync(
            connection, transaction,
            "UPDATE number_sequence SET next_val = " + (billNo + 1).ToString(CultureInfo.InvariantCulture)
            + " WHERE doc_type = 'SALE';",
            token).ConfigureAwait(false);

        await ExecuteAsync(
            connection, transaction,
            "UPDATE number_sequence SET next_val = " + (shiftSequenceNumber + 1).ToString(CultureInfo.InvariantCulture)
            + " WHERE doc_type = 'SHIFT';",
            token).ConfigureAwait(false);

        // The final on-hand balance after every historical sale line posted against it - the
        // opening quantity less whatever this seed sold, never a SUM(stock_movement) read on a
        // live path (CLAUDE.md invariant 3): kept in step here, once, exactly as
        // SqliteStockLedger.PostAsync keeps it in step for a real sale, one movement at a time.
        await ExecuteAsync(
            connection, transaction,
            """
            UPDATE stock_balance
               SET qty_base = qty_base - COALESCE((
                     SELECT -SUM(qty_base) FROM stock_movement
                      WHERE stock_movement.product_variant_id = stock_balance.product_variant_id
                        AND stock_movement.movement_type = 'SALE'
                        AND stock_movement.id >= $first_id
                   ), 0)
             WHERE product_variant_id >= $variant_base;
            """,
            token,
            ("$first_id", StockMovementIdBase),
            ("$variant_base", VariantIdBase)).ConfigureAwait(false);
    }

    /// <summary>
    /// A popularity spread, not a flat one (AC-18's "realistic distribution"): roughly a third of
    /// scans land on the first 5% of the catalogue, the rest spread across the remainder - close
    /// enough to the shelf a hardware shop actually has, without needing a real sales history to
    /// derive it from.
    /// </summary>
    private static int NextVariantIndex(Random random, int skuCount)
    {
        var hotCount = Math.Max(1, skuCount / 20);
        return random.Next(0, 100) < 35 ? random.Next(0, hotCount) : random.Next(0, skuCount);
    }

    private static async Task FlushAsync(
        DbConnection connection, DbTransaction transaction, string insertPrefix, List<string> rows, CancellationToken token)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder(insertPrefix);
        builder.AppendJoin(',', rows);
        builder.Append(';');

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = builder.ToString();
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        rows.Clear();
    }

    private static async Task ExecuteBatchedAsync(
        DbConnection connection,
        DbTransaction transaction,
        string insertPrefix,
        int count,
        int batchSize,
        Func<int, string> rowSql,
        CancellationToken token)
    {
        var builder = new StringBuilder();
        var rowsInBatch = 0;

        for (var i = 0; i < count; i++)
        {
            if (rowsInBatch == 0)
            {
                builder.Clear();
                builder.Append(insertPrefix);
            }
            else
            {
                builder.Append(',');
            }

            builder.Append(rowSql(i));
            rowsInBatch++;

            if (rowsInBatch == batchSize || i == count - 1)
            {
                builder.Append(';');

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = builder.ToString();
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

                rowsInBatch = 0;
            }
        }
    }

    private static async Task ExecuteAsync(
        DbConnection connection, DbTransaction transaction, string sql, CancellationToken token,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<long?> ScalarAsync(
        DbConnection connection, DbTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}

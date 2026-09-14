using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// Stock take (SRS FR-4 stock take, AC-10, task P2-T10), through the real SQLite database
/// <see cref="SaleFixture"/> composes.
/// </summary>
public sealed class StockTakeServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task AC_10_AStockTakeAcrossOneCategoryProducesACorrectVarianceReportAndPostsCorrectionsInOneBatch()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (fixingsCategoryId, timberCategoryId) = await SeedCategoriesAsync(fixture);

        // Two products in the scoped category, one outside it - the scope must resolve only the
        // first two.
        var (inScopeA, _, _) = await SeedProductAsync(fixture, "ST-AC10-A", fixingsCategoryId);
        var (inScopeB, _, _) = await SeedProductAsync(fixture, "ST-AC10-B", fixingsCategoryId);
        var (outOfScope, _, _) = await SeedProductAsync(fixture, "ST-AC10-C", timberCategoryId);

        await PostOpeningStockAsync(fixture, inScopeA, pieceUomId, 100m, 2.00m);
        await PostOpeningStockAsync(fixture, inScopeB, pieceUomId, 50m, 5.00m);
        await PostOpeningStockAsync(fixture, outOfScope, pieceUomId, 30m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + fixingsCategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt));

        started.LineCount.Should().Be(2, "the scope must resolve exactly the two in-category variants");

        // A physically counts short by 8 (shrinkage, value impact 8 x 2.00 = 16.00), B counts
        // exactly right (zero variance - nothing to post for it).
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, inScopeA, 92m));
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, inScopeB, 50m));

        var report = await stockTakes.BuildVarianceReportAsync(started.StockTakeId);
        report.Lines.Should().HaveCount(2);

        // Sorted by absolute value impact descending: the shrinkage line (16.00) before the
        // zero-variance line (0.00).
        report.Lines[0].ProductVariantId.Should().Be(inScopeA);
        report.Lines[0].Variance!.Value.Value.Should().Be(-8m);
        report.Lines[0].Value.Should().Be(Money.FromDecimal(-16.00m));
        report.Lines[1].ProductVariantId.Should().Be(inScopeB);
        report.Lines[1].Value.Should().Be(Money.Zero);
        report.TotalValue.Should().Be(Money.FromDecimal(-16.00m));

        var posted = await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, StartedAt.AddHours(1)));

        // Only the non-zero-variance line posts a movement; the zero-variance line is skipped.
        posted.MovementsPosted.Should().Be(1);
        posted.LinesSkipped.Should().Be(1);

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND ref_doc_id = " + started.StockTakeId + ";"))
            .Should().Be(1);

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + inScopeA + ";"))
            .Should().Be(Quantity.FromDecimal(92m, pieceUomId).ToScaled().ToString(), "the shrinkage line lands on the counted figure");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + inScopeB + ";"))
            .Should().Be(Quantity.FromDecimal(50m, pieceUomId).ToScaled().ToString(), "no correction needed - the count matched exactly");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + outOfScope + ";"))
            .Should().Be(Quantity.FromDecimal(30m, pieceUomId).ToScaled().ToString(), "out of scope, untouched");

        (await fixture.ScalarAsync("SELECT status FROM stock_take WHERE id = " + started.StockTakeId + ";"))
            .Should().Be("POSTED");
    }

    [Fact]
    public async Task P2_T10_ItemsSoldDuringTheCountEndAtTheArithmeticallyCorrectFinalBalance()
    {
        // The task's own named risk: a naive absolute-quantity post would wipe out a sale rung up
        // between the count and the batch post. Freeze at 100, count finds 95 (5 units of real
        // shrinkage discovered at count time), then 10 more sell *after* the count but *before*
        // the batch is posted - the posted correction must still land on the true, fresh count a
        // recount would show right now (85 = 95 counted - 10 sold since), never 100 + variance
        // (95) and never a naive absolute overwrite to 95 that would silently erase the sale.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantId, _, _) = await SeedProductAsync(fixture, "ST-SALE-A", categoryId);

        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 100m, 3.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt));

        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantId, 95m));

        // A sale for 10 units, posted directly through the same ledger door a real sale goes
        // through (CLAUDE.md invariant 3) - happening strictly after the count, before the post.
        await PostSaleAsync(fixture, variantId, pieceUomId, -10m);

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Quantity.FromDecimal(90m, pieceUomId).ToScaled().ToString(), "the sale posted normally, ahead of the stock take batch");

        await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, StartedAt.AddHours(2)));

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(
                Quantity.FromDecimal(85m, pieceUomId).ToScaled().ToString(),
                "85 is what a fresh physical count would show right now (95 counted, less the 10 "
                + "sold since) - not 95 (100 + variance naively re-applied) and not 90 (the sale "
                + "left untouched, as if the count found nothing wrong)");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND ref_doc_id = " + started.StockTakeId + ";"))
            .Should().Be(1);
    }

    [Fact]
    public async Task P2_T10_PartialCountsCanBeSavedAndResumedAcrossSessions()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantA, _, _) = await SeedProductAsync(fixture, "ST-PARTIAL-A", categoryId);
        var (variantB, _, _) = await SeedProductAsync(fixture, "ST-PARTIAL-B", categoryId);

        await PostOpeningStockAsync(fixture, variantA, pieceUomId, 10m, 1.00m);
        await PostOpeningStockAsync(fixture, variantB, pieceUomId, 20m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt));

        // Session 1: only the first line is counted.
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantA, 9m));

        var midway = await stockTakes.FindByIdAsync(started.StockTakeId);
        midway!.Status.Should().Be("OPEN");
        midway.Lines.Single(l => l.ProductVariantId == variantA).CountedQty.Should().NotBeNull();
        midway.Lines.Single(l => l.ProductVariantId == variantB).CountedQty.Should().BeNull();

        // Session 2 (a later, independent call): the second line is counted, and the first is
        // corrected after a miscount - a later call for the same variant overwrites, not
        // accumulates.
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantB, 18m));
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantA, 10m));

        var resumed = await stockTakes.FindByIdAsync(started.StockTakeId);
        resumed!.Lines.Single(l => l.ProductVariantId == variantA).CountedQty!.Value.Value.Should().Be(10m);
        resumed.Lines.Single(l => l.ProductVariantId == variantA).Variance!.Value.Value.Should().Be(0m);
        resumed.Lines.Single(l => l.ProductVariantId == variantB).CountedQty!.Value.Value.Should().Be(18m);
    }

    [Fact]
    public async Task P2_T10_AnUncountedLineIsSkippedAtPostingRatherThanBlockingTheBatch()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (countedVariant, _, _) = await SeedProductAsync(fixture, "ST-UNCOUNTED-A", categoryId);
        var (uncountedVariant, _, _) = await SeedProductAsync(fixture, "ST-UNCOUNTED-B", categoryId);

        await PostOpeningStockAsync(fixture, countedVariant, pieceUomId, 5m, 1.00m);
        await PostOpeningStockAsync(fixture, uncountedVariant, pieceUomId, 7m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt));

        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, countedVariant, 4m));

        var posted = await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, StartedAt.AddHours(1)));

        posted.MovementsPosted.Should().Be(1);
        posted.LinesSkipped.Should().Be(1, "the never-counted line is left alone, not treated as a zero count");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + uncountedVariant + ";"))
            .Should().Be(Quantity.FromDecimal(7m, pieceUomId).ToScaled().ToString(), "untouched - nobody ever checked the shelf");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND product_variant_id = " + uncountedVariant + ";"))
            .Should().Be(0);
    }

    [Fact]
    public async Task P2_T10_AbandoningAStockTakePostsNothingAndLeavesStockUntouched()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantId, _, _) = await SeedProductAsync(fixture, "ST-ABANDON-A", categoryId);

        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 40m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt));

        // A big apparent shrinkage counted, then the count itself is abandoned (a damaged
        // count sheet, a wrong scope, whatever the reason) - none of this may reach stock.
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantId, 1m));

        var abandoned = await stockTakes.AbandonAsync(new AbandonStockTakeCommand(
            started.StockTakeId, "Wrong scope - recount as LOCATION instead", StartedAt.AddHours(1)));

        abandoned.StockTakeId.Should().Be(started.StockTakeId);

        (await fixture.ScalarAsync("SELECT status FROM stock_take WHERE id = " + started.StockTakeId + ";"))
            .Should().Be("ABANDONED");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Quantity.FromDecimal(40m, pieceUomId).ToScaled().ToString(), "stock is exactly what it was before the count");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND ref_doc_id = " + started.StockTakeId + ";"))
            .Should().Be(0, "the abandon path posts nothing (task P2-T10)");
    }

    [Fact]
    public async Task P2_T10_AnAbandonedStockTakeCannotLaterBePosted()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantId, _, _) = await SeedProductAsync(fixture, "ST-REOPEN-A", categoryId);
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 4m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedAt));
        await stockTakes.AbandonAsync(new AbandonStockTakeCommand(started.StockTakeId, "changed my mind"));

        var act = () => stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task P2_T10_PostingIsOneTransactionAFailureRollsBackTheWholeBatch()
    {
        // Proves the atomicity mechanism the "kill mid-post" risk depends on: every movement, the
        // status change and the audit row are written inside one
        // IUnitOfWork.ExecuteInTransactionAsync block, so an interruption anywhere inside it rolls
        // back everything already done in this call - never a half-posted batch. A real SIGKILL
        // proof over a live process is HW-track territory (the same split AC-15's own software
        // half draws against its on-terminal half); this proves the same rollback mechanism
        // deterministically, in-process, via a cancelled token.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantA, _, _) = await SeedProductAsync(fixture, "ST-KILL-A", categoryId);
        var (variantB, _, _) = await SeedProductAsync(fixture, "ST-KILL-B", categoryId);

        await PostOpeningStockAsync(fixture, variantA, pieceUomId, 10m, 1.00m);
        await PostOpeningStockAsync(fixture, variantB, pieceUomId, 10m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedAt));

        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantA, 8m));
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantB, 6m));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Nothing from the interrupted batch reached the database: no STOCK_TAKE movement for
        // either line, the balances are exactly what they were, and the stock take is still OPEN
        // to be posted again once whatever interrupted it is resolved.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND ref_doc_id = " + started.StockTakeId + ";"))
            .Should().Be(0);

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantA + ";"))
            .Should().Be(Quantity.FromDecimal(10m, pieceUomId).ToScaled().ToString());
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantB + ";"))
            .Should().Be(Quantity.FromDecimal(10m, pieceUomId).ToScaled().ToString());

        (await fixture.ScalarAsync("SELECT status FROM stock_take WHERE id = " + started.StockTakeId + ";"))
            .Should().Be("OPEN");

        // And a retry, uninterrupted, now succeeds cleanly.
        var posted = await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, StartedAt.AddHours(1)));
        posted.MovementsPosted.Should().Be(2);
    }

    [Fact]
    public async Task AC_17_ACashierMayStartAndCountButNotPostOrAbandon()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantId, _, _) = await SeedProductAsync(fixture, "ST-ROLE-A", categoryId);
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 12m, 1.00m);

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("counterstaff", "Counter Staff", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("counterstaff", "counter1")).Succeeded.Should().BeTrue();

        var stockTakes = fixture.Resolve<IStockTakeService>();

        // "check stock" capability: a cashier can start a count and record what is on the shelf.
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedAt));
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantId, 11m));

        // Value is owner-only information - stripped for a cashier session, never merely hidden.
        var report = await stockTakes.BuildVarianceReportAsync(started.StockTakeId);
        report.Lines.Single().Value.Should().BeNull();
        report.TotalValue.Should().BeNull();

        // Posting and abandoning are owner-only.
        var post = () => stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId));
        await post.Should().ThrowAsync<NotAuthorisedException>();

        var abandon = () => stockTakes.AbandonAsync(new AbandonStockTakeCommand(started.StockTakeId, "any reason"));
        await abandon.Should().ThrowAsync<NotAuthorisedException>();
    }

    [Fact]
    public async Task FR_4_AScopeMatchingNoActiveVariantIsRefusedAndWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var act = () => stockTakes.StartAsync(new StartStockTakeCommand("CATEGORY:999999", StartedAt));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_take;")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_take_line;")).Should().Be(0);

        // The number allocation and the "no variants matched" check both run inside the same
        // ExecuteInTransactionAsync block (SqliteUnitOfWork's re-entrant ambient transaction), so
        // the failed attempt must not have consumed a STOCK_TAKE number either - "writes nothing"
        // means nothing, not merely no stock_take row.
        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'STOCK_TAKE';"))
            .Should().Be("1", "a refused stock take must not consume a document number");

        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'STOCK_TAKE_STARTED';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_4_AMalformedScopeIsRejectedBeforeConsumingADocumentNumber()
    {
        // StockTakeService.StartAsync's own remarks: the scope is parsed and canonicalised before
        // the transaction opens at all, so a typo in the scope string must fail before anything -
        // including a STOCK_TAKE number - is ever allocated. Distinct from the "well-formed scope,
        // matches nothing" case above, which fails *inside* the transaction instead.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var act = () => stockTakes.StartAsync(new StartStockTakeCommand("NOT-A-SCOPE", StartedAt));

        await act.Should().ThrowAsync<ArgumentException>();
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_take;")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'STOCK_TAKE';"))
            .Should().Be("1", "a malformed scope must not consume a document number");
    }

    [Fact]
    public async Task P2_T10_RecordingACountOnAStockTakeThatIsNotOpenIsRefused()
    {
        // RecordCountAsync's own doc comment: "for as long as the stock take stays OPEN" - a count
        // sheet that has already been posted or abandoned is closed for editing, so a scanner
        // still pointed at it (a stale screen, a race with the owner posting) must be refused
        // rather than silently mutating a terminal document.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantId, _, _) = await SeedProductAsync(fixture, "ST-CLOSED-A", categoryId);
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 6m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedAt));

        await stockTakes.AbandonAsync(new AbandonStockTakeCommand(started.StockTakeId, "wrong scope"));

        var act = () => stockTakes.RecordCountAsync(
            new RecordStockTakeCountCommand(started.StockTakeId, variantId, 5m));

        await act.Should().ThrowAsync<InvalidOperationException>();

        (await fixture.ScalarAsync(
            "SELECT counted_qty FROM stock_take_line WHERE stock_take_id = " + started.StockTakeId
            + " AND product_variant_id = " + variantId + ";"))
            .Should().BeNull("the abandoned take's line must not pick up a count after the fact");
    }

    [Fact]
    public async Task P2_T10_PostingAStockTakeWithNoCountedLinesPostsCleanlyWithZeroMovements()
    {
        // Every line frozen but never counted (the count sheet was printed and the take was
        // posted straight back out, or every physical count matched nothing worth entering) - this
        // must not be an error. PostAsync's own remarks: an uncounted line is left alone, and
        // nothing in "Do this" #4 requires at least one correction to exist before a batch can
        // close.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureStockTakeSeriesAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (categoryId, _) = await SeedCategoriesAsync(fixture);
        var (variantA, _, _) = await SeedProductAsync(fixture, "ST-EMPTY-A", categoryId);
        var (variantB, _, _) = await SeedProductAsync(fixture, "ST-EMPTY-B", categoryId);

        await PostOpeningStockAsync(fixture, variantA, pieceUomId, 3m, 1.00m);
        await PostOpeningStockAsync(fixture, variantB, pieceUomId, 4m, 1.00m);

        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedAt));

        var posted = await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, StartedAt.AddHours(1)));

        posted.MovementsPosted.Should().Be(0);
        posted.LinesSkipped.Should().Be(2);

        (await fixture.ScalarAsync("SELECT status FROM stock_take WHERE id = " + started.StockTakeId + ";"))
            .Should().Be("POSTED", "a stock take with nothing to correct still closes cleanly");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE' AND ref_doc_id = " + started.StockTakeId + ";"))
            .Should().Be(0);

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantA + ";"))
            .Should().Be(Quantity.FromDecimal(3m, pieceUomId).ToScaled().ToString(), "untouched - nothing was counted");
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantB + ";"))
            .Should().Be(Quantity.FromDecimal(4m, pieceUomId).ToScaled().ToString(), "untouched - nothing was counted");
    }

    private static Task<bool> ConfigureStockTakeSeriesAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("STOCK_TAKE", "ST-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    private static async Task<(long FixingsCategoryId, long TimberCategoryId)> SeedCategoriesAsync(SaleFixture fixture)
    {
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var fixingsId = await categories.CreateAsync(new SaveCategoryCommand("Fixings-" + Guid.NewGuid().ToString("N")[..8], null));
        var timberId = await categories.CreateAsync(new SaveCategoryCommand("Timber-" + Guid.NewGuid().ToString("N")[..8], null));
        return (fixingsId, timberId);
    }

    private static async Task<(long VariantId, long ProductId, long PieceUomId)> SeedProductAsync(
        SaleFixture fixture, string code, long categoryId, decimal sellingPrice = 1.00m)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Product " + code,
            NameAlt: null,
            CategoryId: categoryId,
            BrandId: null,
            pieceUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,
            ConfirmDuplicate: true));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(sellingPrice)));

        return (variantId, productId, pieceUomId);
    }

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, uomId),
            Money.FromDecimal(unitCost),
            "OPENING",
            RefDocId: null,
            userId,
            StartedAt));
    }

    /// <summary>
    /// Posts a sale movement directly through the ledger door a real sale goes through (CLAUDE.md
    /// invariant 3) - used to simulate trading continuing during a count without pulling in the
    /// whole sales-screen pipeline this test has no other use for.
    /// </summary>
    private static async Task PostSaleAsync(
        SaleFixture fixture, long variantId, long uomId, decimal signedQuantity)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "SALE",
            Quantity.FromDecimal(signedQuantity, uomId),
            Money.Zero,
            "SALE",
            RefDocId: null,
            userId,
            StartedAt.AddMinutes(30)));
    }
}

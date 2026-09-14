using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// Manual stock adjustments and damage write-offs (SRS FR-4, NFR-S2, task P2-T08): owner-only,
/// reason mandatory, valued at the variant's own moving-average cost, and fully audited. Runs
/// against the real SQLite file <see cref="SaleFixture"/> composes, never the in-memory provider.
/// </summary>
public sealed class PostAdjustmentTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    // ---- Done when #1: a cashier cannot reach the adjustment path -----------------------------

    [Fact]
    public async Task AC_17_ACashierCannotPostAnAdjustmentOrDamageWriteOffButAnOwnerCan()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var adjust = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: null, "Cashier trying to adjust", PostedAt));
        var damage = async () => await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 5m, "Cashier trying to write off", PostedAt));

        await adjust.Should().ThrowAsync<NotAuthorisedException>();
        await damage.Should().ThrowAsync<NotAuthorisedException>();

        // The refusal happens in front of the service - nothing was read, written or partly done.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action IN ('STOCK_ADJUSTMENT_POSTED','STOCK_DAMAGE_POSTED');"))
            .Should().Be(0);

        // Hand the till back to the owner: the same door, called the same way, succeeds.
        await authentication.LogOutAsync();
        (await authentication.LogInAsync(SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword))
            .Succeeded.Should().BeTrue();

        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: null, "Owner correcting the count", PostedAt));

        result.Delta.Value.Should().Be(5m);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type = 'ADJUSTMENT';")).Should().Be(1);
    }

    // ---- Done when #2: correct sign, cost and balance_after ------------------------------------

    [Fact]
    public async Task FR_4_APositiveDeltaAdjustmentPostsAnInboundMovementAndLeavesCostAvgBitIdentical()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        // Seeded shelf: 100 pieces @ 9.0000.
        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 15m, TargetQuantity: null, "Stock count correction", PostedAt));

        result.MovementType.Should().Be(AdjustmentTypes.AdjustmentToken);
        result.Delta.Value.Should().Be(15m);
        result.QtyBefore.Value.Should().Be(100m);
        result.QtyAfter.Value.Should().Be(115m);
        result.UnitCost.Should().Be(Money.FromDecimal(9.00m));
        result.Warning.Should().BeNull("15 x 9.00 = 135.00, well under the default Rs 5000 threshold");

        var movement = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || unit_cost || '|' || balance_after || '|' "
            + "|| ref_doc_type || '|' || COALESCE(ref_doc_id,'-') || '|' || note || '|' || user_id "
            + "FROM stock_movement WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");
        movement.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"ADJUSTMENT|150000|90000|1150000|ADJUSTMENT|-|Stock count correction|{userId}"));

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("1150000");

        // The non-perturbation property: an inbound ADJUSTMENT never touches the average - bit
        // for bit identical to what it was before, not merely close to it.
        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("90000", "a found box of the same items must never be allowed to look like a purchase to the moving average");
    }

    [Fact]
    public async Task FR_4_ANegativeDeltaAdjustmentPostsAnOutboundMovementWithTheCorrectSignCostAndBalance()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: -20m, TargetQuantity: null, "Theft or shrinkage", PostedAt));

        result.MovementType.Should().Be(AdjustmentTypes.AdjustmentToken);
        result.Delta.Value.Should().Be(-20m);
        result.QtyBefore.Value.Should().Be(100m);
        result.QtyAfter.Value.Should().Be(80m);
        result.UnitCost.Should().Be(Money.FromDecimal(9.00m), "an outbound movement snapshots what was already on the shelf");
        result.Warning.Should().BeNull("the warning only ever applies to an inbound movement");

        var movement = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || unit_cost || '|' || balance_after "
            + "FROM stock_movement WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");
        movement.Should().Be("-200000|90000|800000");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("800000");
        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("90000", "an outbound movement never moves the moving average");
    }

    [Fact]
    public async Task FR_4_ATargetQuantityBelowTheCurrentBalancePostsTheCorrectOutboundDelta()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: null, TargetQuantity: 70m, "Stock count correction", PostedAt));

        result.Delta.Value.Should().Be(-30m, "100 counted down to 70 is a decrease of 30");
        result.QtyBefore.Value.Should().Be(100m);
        result.QtyAfter.Value.Should().Be(70m);

        var movement = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || balance_after FROM stock_movement "
            + "WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");
        movement.Should().Be("-300000|700000");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("700000");
    }

    [Fact]
    public async Task FR_4_ATargetQuantityAboveTheCurrentBalancePostsTheCorrectInboundDeltaAndLeavesCostAvgBitIdentical()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: null, TargetQuantity: 140m, "Stock count correction", PostedAt));

        result.Delta.Value.Should().Be(40m, "100 counted up to 140 is an increase of 40");
        result.QtyBefore.Value.Should().Be(100m);
        result.QtyAfter.Value.Should().Be(140m);

        var movement = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || unit_cost || '|' || balance_after FROM stock_movement "
            + "WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");
        movement.Should().Be("400000|90000|1400000");

        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("90000", "an inbound adjustment at the variant's own average cost leaves the average bit-identical");
    }

    [Fact]
    public async Task FR_4_DamageWriteOffPostsAnOutboundMovementAtCurrentAverageCostAndNeverSetsTheWarning()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var result = await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 25m, "Damaged in store", PostedAt));

        result.MovementType.Should().Be(AdjustmentTypes.DamageToken);
        result.Delta.Value.Should().Be(-25m, "a damage write-off is always a decrease");
        result.QtyBefore.Value.Should().Be(100m);
        result.QtyAfter.Value.Should().Be(75m);
        result.UnitCost.Should().Be(Money.FromDecimal(9.00m), "damage is written off at current average cost, not a guess");
        result.Warning.Should().BeNull("the GRN warning never applies to an outbound movement");

        var movement = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || unit_cost || '|' || balance_after || '|' || ref_doc_type "
            + "FROM stock_movement WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");
        movement.Should().Be("DAMAGE|-250000|90000|750000|DAMAGE");

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("750000");
        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("90000");
    }

    // ---- Done when #3: reason is mandatory and appears in the audit log -----------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FR_4_ABlankOrWhitespaceReasonIsRefusedForBothAdjustAndWriteOffDamage(string blankReason)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var adjust = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: null, blankReason, PostedAt));
        var damage = async () => await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 5m, blankReason, PostedAt));

        await adjust.Should().ThrowAsync<InvalidOperationException>().WithMessage("*needs a reason*");
        await damage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*needs a reason*");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("1000000", "the refusal must land before anything is touched");
    }

    [Fact]
    public async Task FR_4_AValidReasonAppearsExactlyInTheAuditLogWithTheCorrectBeforeAndAfterJson()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        // Free text, deliberately not one of the configured reasons - proving the door accepts
        // free text as well as a configured reason (the "or" reading of task P2-T08's own wording).
        await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 15m, TargetQuantity: null, "Found extra stock on the top shelf", PostedAt));

        var adjustmentAudit = await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type || '|' || entity_id || '|' || reason || '|' || user_id "
            + "|| '|' || before_json || '|' || after_json FROM audit_log WHERE action = 'STOCK_ADJUSTMENT_POSTED';");
        adjustmentAudit.Should().Be(
            "STOCK_ADJUSTMENT_POSTED|product_variant|" + variantId.ToString(CultureInfo.InvariantCulture)
            + "|Found extra stock on the top shelf|" + userId.ToString(CultureInfo.InvariantCulture)
            + "|{\"qty_base\":1000000,\"cost_avg\":90000}|{\"qty_base\":1150000,\"cost_avg\":90000}");

        await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 25m, "Damaged in store", PostedAt));

        var damageAudit = await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type || '|' || entity_id || '|' || reason "
            + "|| '|' || before_json || '|' || after_json FROM audit_log WHERE action = 'STOCK_DAMAGE_POSTED';");
        damageAudit.Should().Be(
            "STOCK_DAMAGE_POSTED|product_variant|" + variantId + "|Damaged in store"
            + "|{\"qty_base\":1150000,\"cost_avg\":90000}|{\"qty_base\":900000,\"cost_avg\":90000}");
    }

    // ---- Risks: warn above the configurable GRN threshold, never block -------------------------

    [Fact]
    public async Task P2_T08_AnInboundAdjustmentBelowTheThresholdCarriesNoWarning()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        // 10 x 9.00 = 90.00, far under the default Rs 5000 threshold.
        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 10m, TargetQuantity: null, "Stock count correction", PostedAt));

        result.Warning.Should().BeNull();
    }

    [Fact]
    public async Task P2_T08_AnInboundAdjustmentAboveTheThresholdWarnsButStillPostsSuccessfully()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);
        var threshold = fixture.Resolve<ISettings>().Policy.AdjustmentGrnWarningThreshold;
        threshold.Should().Be(Money.FromDecimal(5000m), "the default policy this test relies on");

        var costAvgBeforeScaled = await fixture.ScalarAsync(
            "SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        var costAvgBefore = Money.FromScaled(long.Parse(costAvgBeforeScaled!, CultureInfo.InvariantCulture));
        var expectedValue = costAvgBefore.Multiply(600m);
        (expectedValue > threshold).Should().BeTrue("600 x 9.00 = 5400.00 must actually clear the default threshold");

        // Never blocks: the call succeeds and the movement lands, exactly as an adjustment below
        // the threshold would (CLAUDE.md invariant 7's "never block" spirit).
        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 600m, TargetQuantity: null, "Stock count correction", PostedAt));

        result.Warning.Should().NotBeNull();
        result.Warning.Should().Contain("GRN");
        result.Warning.Should().Contain(expectedValue.ToString());
        result.Warning.Should().Contain(threshold.ToString());

        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("7000000", "the warning is a nudge, never a refusal - the movement posted in full");
    }

    [Fact]
    public async Task P2_T08_AnOutboundAdjustmentNeverWarnsRegardlessOfItsValue()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        // -90 x 9.00 = -810.00 in magnitude terms would clear a much lower threshold if the
        // outbound guard were missing; BuildWarning's own !delta.IsPositive check must still hold.
        var result = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: -90m, TargetQuantity: null, "Theft or shrinkage", PostedAt));

        result.Warning.Should().BeNull("the threshold only ever nudges an inbound movement towards a GRN");
    }

    // ---- RequireExactlyOneQuantityMode ----------------------------------------------------------

    [Fact]
    public async Task FR_4_GivingBothAQuantityDeltaAndATargetQuantityIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: 105m, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not both and not neither*");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
    }

    [Fact]
    public async Task FR_4_GivingNeitherAQuantityDeltaNorATargetQuantityIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: null, TargetQuantity: null, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not both and not neither*");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
    }

    [Fact]
    public async Task FR_4_AZeroQuantityDeltaIsRefusedAsNothingToPost()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 0m, TargetQuantity: null, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nothing to post*");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
    }

    /// <summary>
    /// The race/transaction-boundary proof: unlike the three checks above, a target equal to the
    /// current balance can only be discovered once the balance has been read - which happens
    /// after <c>BEGIN IMMEDIATE</c> has already opened the transaction (the handler's own
    /// remarks). The refusal fires from inside the open transaction, and must still leave nothing
    /// behind.
    /// </summary>
    [Fact]
    public async Task FR_4_ATargetEqualToTheCurrentBalanceIsRefusedFromInsideTheOpenTransactionAndNothingIsWritten()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: null, TargetQuantity: 100m, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already at the target quantity*");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE entity_id = " + variantId
            + " AND action IN ('STOCK_ADJUSTMENT_POSTED','STOCK_DAMAGE_POSTED');")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("1000000", "the balance read inside the transaction must be left exactly as it was");
    }

    // ---- Unknown / inactive / non-stock variants -------------------------------------------------

    [Fact]
    public async Task FR_4_AnAdjustmentAgainstAnUnknownVariantIsRefusedAndNothingIsWritten()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(999_999, QuantityDelta: 5m, TargetQuantity: null, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = 999999;"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_4_AnAdjustmentAgainstAnInactiveVariantIsRefusedAndNothingIsWritten()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        await fixture.Resolve<IProductMaintenance>().DeactivateVariantAsync(variantId);

        var act = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: null, "Stock count correction", PostedAt));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId
            + " AND movement_type IN ('ADJUSTMENT','DAMAGE');")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be("1000000");
    }

    [Theory]
    [InlineData(ProductType.Service)]
    [InlineData(ProductType.NonInventory)]
    public async Task FR_4_AServiceOrNonInventoryProductVariantCannotBeAdjustedOrWrittenOff(ProductType type)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeedNonStockVariantAsync(fixture, type, "NOSTOCK-" + type);

        var adjust = async () => await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 5m, TargetQuantity: null, "Stock count correction", PostedAt));
        var damage = async () => await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 5m, "Damaged in store", PostedAt));

        await adjust.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not carry stock*");
        await damage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not carry stock*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(0);
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().BeNull("a variant with nothing to hold in stock has never had a balance row created for it");
    }

    // ---- Done when #4: damage is separately reportable -------------------------------------------

    [Fact]
    public async Task P2_T08_DamageIsSeparatelyReportableFromAPlainAdjustmentInTheHistoryQuery()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var adjustment = await fixture.Resolve<IPostAdjustment>().AdjustAsync(
            new AdjustmentCommand(variantId, QuantityDelta: 15m, TargetQuantity: null, "Stock count correction", PostedAt));
        var damage = await fixture.Resolve<IPostAdjustment>().WriteOffDamageAsync(
            new DamageCommand(variantId, 25m, "Damaged in store", PostedAt));

        var history = fixture.Resolve<IAdjustmentHistoryQuery>();

        var damageOnly = await history.ListAsync(new AdjustmentHistoryFilter(Type: AdjustmentType.Damage));
        damageOnly.Should().ContainSingle();
        var damageLine = damageOnly[0];
        damageLine.MovementType.Should().Be(AdjustmentTypes.DamageToken);
        damageLine.ProductVariantId.Should().Be(variantId);
        damageLine.QtyBase.Value.Should().Be(-25m);
        damageLine.BalanceAfter.Value.Should().Be(damage.QtyAfter.Value);
        damageLine.UnitCost.Should().Be(Money.FromDecimal(9.00m), "damage is written off at current average cost");
        damageLine.Reason.Should().Be("Damaged in store");

        var adjustmentOnly = await history.ListAsync(new AdjustmentHistoryFilter(Type: AdjustmentType.Adjustment));
        adjustmentOnly.Should().ContainSingle();
        var adjustmentLine = adjustmentOnly[0];
        adjustmentLine.MovementType.Should().Be(AdjustmentTypes.AdjustmentToken);
        adjustmentLine.QtyBase.Value.Should().Be(15m);
        adjustmentLine.BalanceAfter.Value.Should().Be(adjustment.QtyAfter.Value);

        // Neither filtered list conflates the two movement types with the other.
        damageOnly.Should().NotContain(line => line.MovementType == AdjustmentTypes.AdjustmentToken);
        adjustmentOnly.Should().NotContain(line => line.MovementType == AdjustmentTypes.DamageToken);

        // No filter at all returns both, newest first.
        var both = await history.ListAsync(new AdjustmentHistoryFilter());
        both.Should().HaveCount(2);
        both[0].MovementType.Should().Be(AdjustmentTypes.DamageToken, "the damage write-off was posted second");
        both[1].MovementType.Should().Be(AdjustmentTypes.AdjustmentToken);
    }

    [Fact]
    public async Task AC_17_ACashierCannotReadTheAdjustmentHistory()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var act = async () => await fixture.Resolve<IAdjustmentHistoryQuery>()
            .ListAsync(new AdjustmentHistoryFilter());

        await act.Should().ThrowAsync<NotAuthorisedException>();
    }

    // ---- Shared seeding -----------------------------------------------------------------------

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<IUomMaintenance>();
        var all = await uoms.ListAsync();

        foreach (var uom in all)
        {
            if (uom.Name == "Piece")
            {
                return uom.Id;
            }
        }

        throw new InvalidOperationException("The seeded 'Piece' unit was not found.");
    }

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture)
    {
        var classes = await fixture.Resolve<ITaxClassMaintenance>().ListAsync();

        foreach (var taxClass in classes)
        {
            if (taxClass.Name == "Exempt")
            {
                return taxClass.Id;
            }
        }

        throw new InvalidOperationException("The seeded 'Exempt' tax class was not found.");
    }

    /// <summary>A SERVICE or NON_INVENTORY product's single variant - nothing to hold in stock.</summary>
    private static async Task<long> SeedNonStockVariantAsync(SaleFixture fixture, ProductType type, string code)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Non-stock product " + code,
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceUomId,
            type,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,
            ConfirmDuplicate: true));

        return await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(500.00m)));
    }
}

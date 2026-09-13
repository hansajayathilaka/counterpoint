using System;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Returns;

/// <summary>
/// Unlinked returns end to end: the return-fraud path the SRS itself names (§19) - no original
/// bill, disabled by default, always behind a mandatory owner override with a mandatory reason,
/// refunded at or below today's catalogue price, and flagged in the audit log as its own
/// exception (SRS FR-5.19, NFR-S2, task P2-T03).
/// </summary>
public sealed class CreateUnlinkedReturnTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string Cashier = "priya";
    private const string CashierPassword = "counter1";

    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_5_19_DisabledInSettingsTheHandlerRefusesRatherThanProceeding()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        // AllowUnlinkedReturns is false by default (Q-03) - never overridable (ReturnPolicy's own
        // remarks): a granted, genuine, correctly-scoped token changes nothing here.
        var token = await RequestUnlinkedReturnOverrideAsync(fixture, "Regular customer, no receipt kept.");

        var attempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, token, RefundMethod.Card));

        var exception = await attempt.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.Denied>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;"))
            .Should().Be(0, "a refused attempt writes nothing - not even a consumed return number");
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{ReturnPolicyAuditActions.UnlinkedReturn}';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_5_19_EnabledButAnOverrideGrantedForTheWrongActionDoesNotAuthoriseIt()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        // A real, freshly granted override - just for the wrong action. OverrideToken.TryConsume
        // checks the action it was granted for before it ever checks anything else (the same
        // discipline AnOverrideGrantedForTheWrongActionDoesNotAuthoriseAReturn proves for a linked
        // return's own policy checks).
        var wrongToken = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.CashRefundLimitExceeded, "Wrong action on purpose.", Owner, OwnerPassword));

        var attempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, wrongToken, RefundMethod.Card));

        await attempt.Should().ThrowAsync<ReturnNotEligibleException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);
    }

    [Fact]
    public async Task FR_5_19_EnabledButAnAlreadySpentOverrideCannotAuthoriseASecondReturn()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var token = await RequestUnlinkedReturnOverrideAsync(fixture, "Regular customer, no receipt kept.");

        // Spends the token on a first, successful return.
        await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(await CommandAsync(fixture, token, RefundMethod.Card));

        // The same token again, for a second return: single-use, exactly as every other
        // OverrideToken (SRS FR-1.7's own "one yes authorises one thing" discipline).
        var reuse = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, token, RefundMethod.Card));

        await reuse.Should().ThrowAsync<ReturnNotEligibleException>("the token was already spent by the first call");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(1, "only the first attempt ever committed");
    }

    [Fact]
    public async Task FR_5_19_ABlankReasonIsRejectedEvenWithAValidOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var token = await RequestUnlinkedReturnOverrideAsync(fixture, "Regular customer, no receipt kept.");
        var variantId = await SeededVariantIdAsync(fixture);

        var command = new CreateUnlinkedReturnCommand(
            await SeededCashierUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new UnlinkedReturnLineRequest(
                variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(12.50m), ReturnDisposition.Sellable, "No bill kept")],
            RefundMethod.Card,
            token,
            Reason: "   ");

        var attempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(command);

        await attempt.Should().ThrowAsync<InvalidOperationException>(
            "there is no bill to explain why it has none, so the reason cannot be blank");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);
    }

    [Fact]
    public async Task FR_5_19_ASuccessfulUnlinkedReturnWritesBothTheOverrideGrantAndTheUnlinkedReturnAuditRows()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var token = await RequestUnlinkedReturnOverrideAsync(fixture, "Regular customer, no receipt kept.");

        var created = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, token, RefundMethod.Card));

        // The grant itself, naming both the cashier who asked and the owner who allowed it
        // (SRS FR-1.6) - written by IOwnerOverrideService.RequestAsync, not by this handler.
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}' "
            + $"AND user_id = {token.GrantedByUserId};"))
            .Should().Be(1);

        // The document-side row this task exists to guarantee: on the sale_return itself, so a
        // future exceptions report can select every unlinked return with a plain filter and no
        // JOIN at all (task P2-T03 step 5).
        var saleReturnId = created.SaleReturnId.ToString(CultureInfo.InvariantCulture);

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{ReturnPolicyAuditActions.UnlinkedReturn}' "
            + $"AND entity_type = 'sale_return' AND entity_id = {saleReturnId};"))
            .Should().Be(1);

        // "Queryable as an exception" proven literally: a plain filter on audit_log alone, with no
        // JOIN to sale_return, already names the exact return it belongs to.
        (await fixture.ScalarAsync(
            $"SELECT entity_id FROM audit_log WHERE action = '{ReturnPolicyAuditActions.UnlinkedReturn}';"))
            .Should().Be(saleReturnId);

        // The two rows this whole flow was built to write, and nothing pretending to be a linked
        // return: no bill, no original sale line.
        (await fixture.ScalarAsync(
            $"SELECT original_sale_id FROM sale_return WHERE id = {saleReturnId};"))
            .Should().BeNull("sale_return.original_sale_id is NULL for an unlinked return");
        (await fixture.ScalarAsync(
            $"SELECT sale_line_id FROM sale_return_line WHERE sale_return_id = {saleReturnId};"))
            .Should().BeNull("sale_return_line.sale_line_id is NULL - there is no bill line to point at");
    }

    [Fact]
    public async Task TheRefundAmountCannotExceedTheCurrentSellingPriceButAtOrBelowItSucceeds()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededCashierUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // The shelf price is 12.50 (FirstRunSeeder). 13.00 is the operator typing a higher figure
        // than the shelf is selling at right now - structurally refused (task P2-T03 step 3).
        var overPriced = await RequestUnlinkedReturnOverrideAsync(fixture, "Trying to overpay the refund.");
        var overAttempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            new CreateUnlinkedReturnCommand(
                userId,
                shiftId,
                ReturnedAt,
                [new UnlinkedReturnLineRequest(
                    variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(13.00m), ReturnDisposition.Sellable, "No bill kept")],
                RefundMethod.Card,
                overPriced,
                "No bill kept, regular customer."));

        await overAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current selling price*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0, "the over-priced attempt wrote nothing");

        // Exactly at today's price: allowed.
        var atPrice = await RequestUnlinkedReturnOverrideAsync(fixture, "No bill kept, regular customer.");
        var created = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            new CreateUnlinkedReturnCommand(
                userId,
                shiftId,
                ReturnedAt,
                [new UnlinkedReturnLineRequest(
                    variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(12.50m), ReturnDisposition.Sellable, "No bill kept")],
                RefundMethod.Card,
                atPrice,
                "No bill kept, regular customer."));

        created.TotalRefund.Should().Be(Money.FromDecimal(12.50m));

        // Below today's price - a goodwill part-refund - is also allowed (task P2-T03 step 3's own
        // remarks: "there is deliberately no original price to default to instead").
        var belowPrice = await RequestUnlinkedReturnOverrideAsync(fixture, "Goodwill partial refund.");
        var belowCreated = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            new CreateUnlinkedReturnCommand(
                userId,
                shiftId,
                ReturnedAt,
                [new UnlinkedReturnLineRequest(
                    variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(10.00m), ReturnDisposition.Sellable, "Goodwill")],
                RefundMethod.Card,
                belowPrice,
                "Goodwill partial refund."));

        belowCreated.TotalRefund.Should().Be(Money.FromDecimal(10.00m));
    }

    [Fact]
    public async Task StockIncreasesForASellableLineButNotForADamagedLine()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededCashierUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var qtyBefore = await fixture.CountAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

        var sellableToken = await RequestUnlinkedReturnOverrideAsync(fixture, "No bill kept, sellable condition.");
        await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            new CreateUnlinkedReturnCommand(
                userId,
                shiftId,
                ReturnedAt,
                [new UnlinkedReturnLineRequest(
                    variantId, Quantity.FromDecimal(2m, variantId), Money.FromDecimal(12.50m), ReturnDisposition.Sellable, "Sellable")],
                RefundMethod.Card,
                sellableToken,
                "No bill kept, sellable condition."));

        var qtyAfterSellable = await fixture.CountAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        (qtyAfterSellable - qtyBefore).Should().Be(20000, "2 units, scaled x10 000, restocked");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'RETURN' AND movement_type = 'RETURN_IN';"))
            .Should().Be(1);

        var damagedToken = await RequestUnlinkedReturnOverrideAsync(fixture, "No bill kept, damaged condition.");
        await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            new CreateUnlinkedReturnCommand(
                userId,
                shiftId,
                ReturnedAt,
                [new UnlinkedReturnLineRequest(
                    variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(12.50m), ReturnDisposition.Damaged, "Damaged")],
                RefundMethod.Card,
                damagedToken,
                "No bill kept, damaged condition."));

        var qtyAfterDamaged = await fixture.CountAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        qtyAfterDamaged.Should().Be(qtyAfterSellable, "a DAMAGED unlinked return posts no stock movement at all");

        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'RETURN';"))
            .Should().Be(1, "still only the one RETURN_IN from the SELLABLE return above");
    }

    [Fact]
    public async Task DefaultSettingsRefuseCashAndCreditNoteButAllowCard()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        var cashToken = await RequestUnlinkedReturnOverrideAsync(fixture, "Trying cash.");
        var cashAttempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, cashToken, RefundMethod.Cash));

        await cashAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*allowed_unlinked_refund_methods*", "cash is excluded from the default allow-list");

        var creditNoteToken = await RequestUnlinkedReturnOverrideAsync(fixture, "Trying credit note.");
        var creditNoteAttempt = async () => await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, creditNoteToken, RefundMethod.CreditNote));

        // Refused everywhere today, even though the default allow-list itself names it - P2-T05
        // has not created a credit_note row to back one with yet (RefundMethodMapping's own remarks).
        await creditNoteAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*P2-T05*");

        var cardToken = await RequestUnlinkedReturnOverrideAsync(fixture, "Card refund.");
        var created = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, cardToken, RefundMethod.Card));

        created.TotalRefund.IsPositive.Should().BeTrue();

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(1, "only the card attempt ever committed");
        (await fixture.ScalarAsync(
            $"SELECT tender_type FROM payment WHERE sale_return_id = {created.SaleReturnId};"))
            .Should().Be("CARD");
    }

    [Fact]
    public async Task WideningAllowedUnlinkedRefundMethodsInSettingsPermitsACashRefund()
    {
        await using var fixture = await SignedInAsCashierAsync();
        await EnableUnlinkedReturnsAsync(fixture);
        await SeedReturnNumberSequenceAsync(fixture);

        await AsOwnerAsync(fixture, async () =>
        {
            await fixture.Resolve<ISettings>().UpdateAsync(s => s with
            {
                Policy = s.Policy with { AllowedUnlinkedRefundMethods = [RefundMethod.Cash, RefundMethod.Card] },
            });
        });

        var token = await RequestUnlinkedReturnOverrideAsync(fixture, "Cash now allowed by policy.");
        var created = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(
            await CommandAsync(fixture, token, RefundMethod.Cash));

        created.TotalRefund.IsPositive.Should().BeTrue();
        (await fixture.ScalarAsync(
            $"SELECT tender_type FROM payment WHERE sale_return_id = {created.SaleReturnId};"))
            .Should().Be("CASH");
    }

    private static Task<OverrideToken> RequestUnlinkedReturnOverrideAsync(SaleFixture fixture, string reason) =>
        fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.UnlinkedReturn, reason, Owner, OwnerPassword));

    private static async Task<CreateUnlinkedReturnCommand> CommandAsync(
        SaleFixture fixture, OverrideToken token, RefundMethod refundMethod)
    {
        var variantId = await SeededVariantIdAsync(fixture);

        return new CreateUnlinkedReturnCommand(
            await SeededCashierUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new UnlinkedReturnLineRequest(
                variantId, Quantity.FromDecimal(1m, variantId), Money.FromDecimal(12.50m), ReturnDisposition.Sellable, "No bill kept")],
            refundMethod,
            token,
            "No bill kept, regular customer.");
    }

    private static Task EnableUnlinkedReturnsAsync(SaleFixture fixture) => AsOwnerAsync(fixture, async () =>
    {
        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { AllowUnlinkedReturns = true } });
    });

    private static async Task AsOwnerAsync(SaleFixture fixture, Func<Task> action)
    {
        var authentication = fixture.Resolve<IAuthenticationService>();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();

        await action();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Cashier, CashierPassword)).Succeeded.Should().BeTrue();
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in - same shape as ReturnPolicyAuthorisationServiceTests.</summary>
    private static async Task<SaleFixture> SignedInAsCashierAsync()
    {
        var fixture = await SaleFixture.CreateAsync();

        try
        {
            await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

            var authentication = fixture.Resolve<IAuthenticationService>();
            await authentication.LogInAsync(Owner, OwnerPassword);

            await fixture.Resolve<IUserAdministration>().CreateAsync(
                new CreateUserCommand(Cashier, "Priya", CashierPassword, Role.Cashier));

            await authentication.LogOutAsync();
            (await authentication.LogInAsync(Cashier, CashierPassword)).Succeeded.Should().BeTrue();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The real till always has this row before a return can exist - see
    /// <c>CreateReturnTests.SeedReturnNumberSequenceAsync</c>'s own remarks.
    /// </summary>
    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededCashierUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync(
            $"SELECT id FROM app_user WHERE username = '{Cashier}' ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}

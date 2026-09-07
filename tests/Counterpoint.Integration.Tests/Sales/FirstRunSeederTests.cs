using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// The first-run seed: enough to ring up one bill, and safe to run on every start
/// (docs/01_DATA_MODEL.md §11).
/// </summary>
public sealed class FirstRunSeederTests
{
    [Fact]
    public async Task DM_04_SeedingPutsExactlyOneOfEachRowTheSalePathNeeds()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM uom;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM product;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM product_variant;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM barcode;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM app_user WHERE role = 'OWNER';")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'OPEN';")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_balance;")).Should().Be(1);

        var sequence = await fixture.ScalarAsync(
            "SELECT prefix || '|' || pattern || '|' || next_val FROM number_sequence WHERE doc_type = 'SALE';");

        sequence.Should().Be(
            "INV-|{prefix}{yyyy}-{n:000000}|1",
            "Q-16's documented default, and the first bill it issues is INV-2026-000001");
    }

    [Fact]
    public async Task DM_04_TheSeededShiftIsNumberedFromTheSequenceAndNotFromARowCount()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        (await fixture.ScalarAsync("SELECT shift_no FROM shift WHERE status = 'OPEN';"))
            .Should().Be("SH-000001");

        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SHIFT';"))
            .Should().Be(
                "2",
                "the seeded shift consumed SH-000001 through number_sequence, so the next shift "
                + "opened by any later code cannot collide with it on shift_no UNIQUE "
                + "(CLAUDE.md invariant 4)");
    }

    [Fact]
    public async Task DM_04_TheOpeningStockIsALedgerMovementAndNotABareBalance()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var opening = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || balance_after || '|' "
            + "|| ref_doc_type || '|' || (ref_doc_id IS NULL) FROM stock_movement;");

        opening.Should().Be(
            "OPENING|1000000|1000000|OPENING|1",
            "100 pieces posted through IStockLedger, answering to no document");

        // Nothing in the projection that the ledger cannot account for: a rebuild has to be able
        // to reproduce it exactly (CLAUDE.md invariant 3, docs/01_DATA_MODEL.md).
        var balance = await fixture.ScalarAsync("SELECT qty_base FROM stock_balance;");
        var replayed = await fixture.ScalarAsync("SELECT SUM(qty_base) FROM stock_movement;");

        replayed.Should().Be(balance);
    }

    [Fact]
    public async Task DM_04_SeedingTwiceChangesNothingAndDoesNotFail()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var seeder = fixture.Resolve<FirstRunSeeder>();

        // The fixture already seeded once, so this is the second run.
        var wroteAnything = await seeder.EnsureSeededAsync();
        await seeder.EnsureSeededAsync();

        wroteAnything.Should().BeFalse("everything was already there");

        (await fixture.CountAsync("SELECT COUNT(*) FROM uom;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM product_variant;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM barcode;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM app_user;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM shift;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM number_sequence;"))
            .Should().Be(2, "SALE and SHIFT (docs/01_DATA_MODEL.md §11)");

        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;"))
            .Should().Be(1, "a second run must not post a second opening count");
        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SHIFT';"))
            .Should().Be("2", "and it must not burn a second shift number either");
    }

    [Fact]
    public async Task NFR_S1_TheSeededOwnerHasNoUsablePassword()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var hash = await fixture.ScalarAsync("SELECT password_hash FROM app_user WHERE role = 'OWNER';");

        hash.Should().NotStartWith(
            "$argon2",
            "there is no default password, ever - the owner's real credentials are created in "
            + "P1-T02 (docs/01_DATA_MODEL.md §11)");
    }
}

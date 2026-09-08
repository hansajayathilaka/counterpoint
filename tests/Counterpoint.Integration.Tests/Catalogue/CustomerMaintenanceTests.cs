using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's customer maintenance (SRS FR-6.1).</summary>
public sealed class CustomerMaintenanceTests
{
    [Fact]
    public async Task FR_6_1_ACustomerCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var id = await customers.CreateAsync(
            new SaveCustomerCommand("Perera Traders", "071-1234567", "Kandy", "TAX-9", "TRADE", Money.FromDecimal(50000m)));

        (await fixture.ScalarAsync($"SELECT type FROM customer WHERE id = {id};")).Should().Be("TRADE");

        await customers.UpdateAsync(
            id, new SaveCustomerCommand("Perera Traders Ltd", "071-1234567", "Kandy", "TAX-9", "RETAIL", Money.Zero));
        (await fixture.ScalarAsync($"SELECT name FROM customer WHERE id = {id};")).Should().Be("Perera Traders Ltd");
        (await fixture.ScalarAsync($"SELECT type FROM customer WHERE id = {id};")).Should().Be("RETAIL");

        await customers.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM customer WHERE id = {id};")).Should().Be("0");

        await customers.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM customer WHERE id = {id};")).Should().Be("1");
    }

    [Fact]
    public async Task AnInvalidCustomerTypeIsRejected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var create = () => customers.CreateAsync(
            new SaveCustomerCommand("Bad Type Co", null, null, null, "WHOLESALE", Money.Zero));

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Retail or Trade*");
    }

    [Fact]
    public async Task DeactivatingACustomerIsAlwaysAllowedEvenWithNoSalesHistory()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var id = await customers.CreateAsync(new SaveCustomerCommand("Walk-in", null, null, null, "RETAIL", Money.Zero));

        await customers.DeactivateAsync(id);

        (await fixture.ScalarAsync($"SELECT active FROM customer WHERE id = {id};")).Should().Be("0");
    }

    [Fact]
    public async Task AnUnusedCustomerCanBeDeletedOutright()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var id = await customers.CreateAsync(new SaveCustomerCommand("Never sold to", null, null, null, "RETAIL", Money.Zero));

        await customers.DeleteAsync(id);

        (await fixture.CountAsync($"SELECT COUNT(*) FROM customer WHERE id = {id};")).Should().Be(0);
    }

    [Fact]
    public async Task DeletingACustomerWithABillRecordedAgainstThemIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var id = await customers.CreateAsync(new SaveCustomerCommand("Regular", null, null, null, "RETAIL", Money.Zero));
        await CreateSaleForCustomerAsync(fixture, id);

        var delete = () => customers.DeleteAsync(id);

        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");
    }

    /// <summary>
    /// Writes a minimal <c>sale</c> row against <paramref name="customerId"/> directly, because
    /// nothing above P1-T04 in the build order wires a customer onto a bill through
    /// <c>ICompleteSale</c> yet (<c>sale.customer_id</c> carries no application-layer write path
    /// before the sales screen and credit accounts arrive).
    /// </summary>
    private static Task CreateSaleForCustomerAsync(SaleFixture fixture, long customerId)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);
            var shiftId = await context.Set<Shift>().Select(row => row.Id).FirstAsync(token);

            context.Add(new Sale
            {
                BillNo = "TEST-CUST-001",
                SoldAt = new DateTimeOffset(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5)),
                BusinessDate = "2026-09-06",
                CustomerId = customerId,
                UserId = userId,
                ShiftId = shiftId,
                Subtotal = Money.FromDecimal(10m),
                LineDiscount = Money.Zero,
                BillDiscount = Money.Zero,
                Tax = Money.Zero,
                Rounding = Money.Zero,
                Total = Money.FromDecimal(10m),
                Cogs = Money.Zero,
                Status = "COMPLETED",
                CancelledBy = null,
                CancelledAt = null,
                Note = null,
                PrevHash = "0000000000000000000000000000000000000000000000000000000000000000000",
                RowHash = "0000000000000000000000000000000000000000000000000000000000000000001",
            });

            await context.SaveChangesAsync(token);
        });
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.Printing;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// The walking skeleton's whole vertical slice, driven from the viewmodel: scan box, line list,
/// total, Pay.
/// </summary>
/// <remarks>
/// The viewmodel is exercised directly rather than through a window, because a window cannot be
/// opened in CI. Everything below it - the Application handlers, the SQLite adapters, the real
/// encrypted file, the outbox and the printer - is the production wiring, composed by
/// <see cref="SaleFixture"/> exactly as <c>Counterpoint.App</c> composes it.
/// </remarks>
public sealed class SalesScreenTests
{
    [Fact]
    public async Task FR_3_28_TypingTheSeededBarcodeAddsALineAndPaySavesTheBillAndQueuesTheReceipt()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var screen = BuildScreen(fixture);

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Lines.Should().ContainSingle();
        screen.Lines[0].Description.Should().Be("Galvanised bolt M8");
        screen.Lines[0].QuantityText.Should().Be("1 pc");
        screen.Total.Should().Be("12.50", "the total comes from the Application layer, not from the screen");
        screen.Barcode.Should().BeEmpty("the scan box clears itself for the next item");

        await screen.PayCommand.ExecuteAsync(null);

        screen.Status.Should().Be("Saved as INV-2026-000001. The receipt is queued.");
        screen.Lines.Should().BeEmpty("the bill is finished");
        screen.Total.Should().Be("0.00");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE bill_no = 'INV-2026-000001';"))
            .Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE status = 'PENDING';"))
            .Should().Be(1);

        // And the outbox worker turns that row into a byte stream through FileReceiptPrinter.
        (await fixture.Resolve<PrintWorker>().DrainAsync()).Should().Be(1);
        (await fixture.ScalarAsync("SELECT status FROM print_job;")).Should().Be("PRINTED");
    }

    [Fact]
    public async Task UI_06_AnUnknownBarcodeIsASentenceOnTheScreenNotAnError()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var screen = BuildScreen(fixture);

        screen.Barcode = "0000000000000";
        await screen.ScanCommand.ExecuteAsync(null);

        screen.Status.Should().Be("No item found for 0000000000000.");
        screen.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task AC_16_ABrokenPrinterIsInvisibleToTheCashierCompletingTheBill()
    {
        await using var fixture = await SaleFixture.CreateAsync(PrinterFailureMode.FailEveryJob);

        var screen = BuildScreen(fixture);

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);
        await screen.PayCommand.ExecuteAsync(null);

        screen.Status.Should().StartWith("Saved as INV-2026-000001");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(1);
    }

    private static SalesViewModel BuildScreen(SaleFixture fixture) => new(
        fixture.Resolve<IScanItem>(),
        fixture.Resolve<IQuoteSale>(),
        fixture.Resolve<ICompleteSale>(),
        fixture.Resolve<ITillSessionProvider>(),
        fixture.Resolve<TimeProvider>());
}

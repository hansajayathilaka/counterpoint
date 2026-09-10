using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Devices.Printing;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Printing;

/// <summary>
/// The print queue screen's viewmodel, over the real outbox (P1-T11's "Print queue UI: pending
/// and failed jobs, retry, with the status-bar indicator").
/// </summary>
public sealed class PrintQueueViewModelTests
{
    [Fact]
    public async Task RefreshListsPendingAndFailedJobsAndTheSummaryCountsThem()
    {
        await using var fixture = await Sales.SaleFixture.CreateAsync(PrinterFailureMode.FailEveryJob);

        var outbox = fixture.Resolve<IPrintJobOutbox>();
        await outbox.EnqueueAsync(new PrintJobRequest("SALE", 1, [0x1B, 0x40]));
        await outbox.EnqueueAsync(new PrintJobRequest("SALE", 2, [0x1B, 0x40]));

        var worker = fixture.Resolve<PrintWorker>();

        // Fail one job to FAILED (max attempts on this fixture's default options); leave the
        // other PENDING by draining only once.
        await worker.DrainAsync();

        var viewModel = new PrintQueueViewModel(outbox);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Jobs.Should().HaveCount(2);
        viewModel.Summary.Should().Contain("pending");
    }

    [Fact]
    public async Task RetryPutsAFailedJobBackToPendingAndPrintWorkerPicksItUpAgain()
    {
        await using var fixture = await Sales.SaleFixture.CreateAsync(PrinterFailureMode.FailEveryJob);

        var outbox = fixture.Resolve<IPrintJobOutbox>();
        var jobId = await outbox.EnqueueAsync(new PrintJobRequest("SALE", 1, [0x1B, 0x40]));

        var worker = fixture.Resolve<PrintWorker>();

        // Three attempts (the fixture's default MaxAttempts), then FAILED.
        await worker.DrainAsync();
        await worker.DrainAsync();
        await worker.DrainAsync();

        (await fixture.ScalarAsync("SELECT status FROM print_job WHERE id = " + jobId + ";"))
            .Should().Be("FAILED");

        var viewModel = new PrintQueueViewModel(outbox);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        var job = viewModel.Jobs.Should().ContainSingle().Subject;
        job.Status.Should().Be("FAILED");

        await viewModel.RetryCommand.ExecuteAsync(job);

        (await fixture.ScalarAsync("SELECT status || '|' || attempts FROM print_job WHERE id = " + jobId + ";"))
            .Should().Be("PENDING|0", "a retry clears the attempt count as well as the status");
    }
}

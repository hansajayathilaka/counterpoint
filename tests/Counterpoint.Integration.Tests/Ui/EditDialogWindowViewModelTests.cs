using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Dialogs;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T11's shared dialog shell viewmodel (SRS UI-05, UI-06, UI-15, AC-23). Proves the
/// header is a pure function of the explicit <see cref="DialogMode"/>/entity/subject a caller
/// passes in, never of whatever the hosted content viewmodel's fields currently hold - the
/// confirmed defect ("the same inline form ... with nothing on screen naming the action or the
/// record") this task replaces.
/// </summary>
public sealed class EditDialogWindowViewModelTests
{
    [Fact]
    public void UI_15_CreateModeHeaderReadsNewEntityEvenWhenContentAlreadyHoldsIdentifyingText()
    {
        // The content viewmodel already holds a non-blank name - as it would once an operator has
        // started typing. If the header were inferred from field content rather than the explicit
        // DialogMode, this could misread as an edit. It must not.
        var content = new TestEditContent { Name = "Fasteners" };

        var viewModel = EditDialogWindowViewModel.ForEdit(DialogMode.Create, "category", subjectDescription: null, content);

        viewModel.HeaderText.Should().Be("New category");
        viewModel.IsDeleteConfirmation.Should().BeFalse();
        viewModel.Content.Should().BeSameAs(content);
    }

    [Fact]
    public void UI_15_EditModeHeaderNamesTheSubjectEvenWhenTheContentFieldIsBlank()
    {
        // The operator has cleared the name field mid-edit. The header must still say which
        // record is being edited - it must not fall back to "New" just because the bound field is
        // momentarily blank. That fallback is exactly the inference this task's header logic
        // never performs: EditDialogWindowViewModel.HeaderText is fixed at construction from
        // DialogMode/entityName/subjectDescription alone and never re-reads the content.
        var content = new TestEditContent { Name = string.Empty };

        var viewModel = EditDialogWindowViewModel.ForEdit(DialogMode.Edit, "category", subjectDescription: "Fasteners", content);

        viewModel.HeaderText.Should().Be("Edit category — Fasteners");
    }

    [Fact]
    public void UI_05_DeleteConfirmationNamesTheSpecificRecordBeforeAnythingHappens()
    {
        var viewModel = EditDialogWindowViewModel.ForDelete("category", "Fasteners");

        viewModel.HeaderText.Should().Be("Delete category");
        viewModel.IsDeleteConfirmation.Should().BeTrue();
        viewModel.Message.Should().Contain("Fasteners");
        viewModel.Content.Should().BeNull();
    }

    [Fact]
    public async Task UI_15_ConfirmingTheDeleteShellClosesWithConfirmedAndNoSideEffectOfItsOwn()
    {
        var viewModel = EditDialogWindowViewModel.ForDelete("category", "Fasteners");
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        await viewModel.PrimaryCommand.ExecuteAsync(null);

        viewModel.Outcome.Should().Be(DialogOutcome.Confirmed);
        closed.Should().BeTrue("the shell must close once the operator confirms");
    }

    [Fact]
    public void UI_01_CancellingRequestsCloseWithCancelledOutcome()
    {
        var content = new TestEditContent { Name = "Fasteners" };
        var viewModel = EditDialogWindowViewModel.ForEdit(DialogMode.Edit, "category", "Fasteners", content);
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.CancelCommand.Execute(null);

        viewModel.Outcome.Should().Be(DialogOutcome.Cancelled);
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task UI_06_ASaveFailureLeavesAPlainLanguageMessageAndTheDialogOpenAsync()
    {
        var content = new TestEditContent
        {
            Name = "Fasteners",
            OnSave = _ => throw new InvalidOperationException("A category named \"Fasteners\" already exists."),
        };
        var viewModel = EditDialogWindowViewModel.ForEdit(DialogMode.Create, "category", null, content);
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        await viewModel.PrimaryCommand.ExecuteAsync(null);

        viewModel.Outcome.Should().Be(DialogOutcome.Cancelled, "nothing was actually saved");
        viewModel.ErrorMessage.Should().Be("A category named \"Fasteners\" already exists.");
        viewModel.Busy.Should().BeFalse();
        closed.Should().BeFalse("a failed save must not close the dialog out from under the operator");
    }

    [Fact]
    public async Task UI_15_ASuccessfulSaveClosesWithConfirmedAsync()
    {
        var content = new TestEditContent { Name = "Fasteners", OnSave = _ => Task.FromResult(true) };
        var viewModel = EditDialogWindowViewModel.ForEdit(DialogMode.Create, "category", null, content);
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        await viewModel.PrimaryCommand.ExecuteAsync(null);

        viewModel.Outcome.Should().Be(DialogOutcome.Confirmed);
        closed.Should().BeTrue();
    }

    /// <summary>A minimal stand-in for a real content viewmodel such as <c>CategoryEditViewModel</c>.</summary>
    private sealed class TestEditContent : ViewModelBase, IEditDialogContent
    {
        public string Name { get; set; } = string.Empty;

        public Func<CancellationToken, Task<bool>>? OnSave { get; set; }

        public Task<bool> SaveAsync(CancellationToken cancellationToken) =>
            OnSave?.Invoke(cancellationToken) ?? Task.FromResult(true);
    }
}

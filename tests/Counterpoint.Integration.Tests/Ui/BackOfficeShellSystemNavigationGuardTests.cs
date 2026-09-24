using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Settings;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T19's own named risk, and the one its own "Risks" section calls the largest
/// behavioural risk in the whole nav-rail set: leaving the System group
/// (<see cref="BackOfficeShellViewModel.SelectedSettingsSection"/>) while
/// <see cref="SettingsViewModel.HasUnsavedChanges"/> is true must show the shared P3-T11
/// confirmation shell first (SRS UI-05), must discard-and-navigate only when confirmed, must
/// leave System selected with the edit intact when cancelled, and must never prompt at all when
/// there is nothing unsaved to lose. Built over a real, signed-in-as-owner <see cref="SaleFixture"/>
/// with the real <see cref="SettingsViewModel"/> wired the same way
/// <c>SettingsScreenTests.Open</c> wires it, never a hand-rolled substitute for either viewmodel -
/// only <see cref="FakeDialogService"/> stands in for the P3-T11 dialog shell itself, because a
/// window cannot be opened in CI.
/// </summary>
public sealed class BackOfficeShellSystemNavigationGuardTests
{
    [Fact]
    public async Task UI_05_NavigatingToCatalogueWithUnsavedSettingsChangesShowsTheConfirmationNamingWhatWillBeDiscardedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";
        settings.HasUnsavedChanges.Should().BeTrue("the edit was made organically through the real settings viewmodel");

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];

        dialogs.ConfirmationRequests.Should().ContainSingle(
            "leaving System with an unsaved edit must ask exactly once, through the shared P3-T11 "
            + "dialog shell, not a bespoke confirmation of its own");

        var request = dialogs.ConfirmationRequests[0];
        request.HeaderText.Should().ContainEquivalentOf("discard");
        request.Message.Should().ContainEquivalentOf(
            "Settings",
            "SRS UI-05 requires the dialog to name what will be discarded, not ask a bare "
            + "\"Are you sure?\"");
    }

    [Fact]
    public async Task UI_05_SelectingOverviewWithUnsavedSettingsChangesAlsoShowsTheConfirmationAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";

        shell.SelectOverviewCommand.Execute(null);

        dialogs.ConfirmationRequests.Should().ContainSingle(
            "the Overview nav item is a second, separate way to leave System, and must be guarded "
            + "exactly the same way SelectedCatalogueSection's own setter is");
    }

    [Fact]
    public async Task UI_05_ConfirmingTheGuardDiscardsTheEditAndCompletesTheNavigationToCatalogueAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);
        dialogs.ConfirmConfirmations = true;

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];

        settings.HasUnsavedChanges.Should().BeFalse(
            "confirming the dialog must discard the edit (Settings.RevertCommand), not merely hide it");
        settings.Shop.Name.Should().BeEmpty("the edit was thrown away, not saved");

        shell.SelectedCatalogueSection.Should().Be(
            BackOfficeShellViewModel.CatalogueSectionNames[0],
            "the attempted navigation must now actually complete");
        shell.SelectedSettingsSection.Should().BeNull("System is no longer the active group");
    }

    [Fact]
    public async Task UI_05_ConfirmingTheGuardFromOverviewDiscardsTheEditAndCompletesTheNavigationToOverviewAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);
        dialogs.ConfirmConfirmations = true;

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";

        shell.SelectOverviewCommand.Execute(null);

        settings.HasUnsavedChanges.Should().BeFalse();
        shell.IsOverviewActive.Should().BeTrue("the attempted navigation to Overview must complete");
        shell.SelectedSettingsSection.Should().BeNull();
        shell.SelectedCatalogueSection.Should().BeNull();
    }

    [Fact]
    public async Task UI_05_CancellingTheGuardKeepsSystemSelectedWithTheEditIntactAndDoesNotNavigateAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);
        dialogs.ConfirmConfirmations = false;

        var systemSection = BackOfficeShellViewModel.SettingsSectionNames[0];
        shell.SelectSettingsSectionCommand.Execute(systemSection);
        settings.Shop.Name = "Nimal Hardware";

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];

        dialogs.ConfirmationRequests.Should().ContainSingle("cancelling still means the dialog was asked");

        shell.SelectedSettingsSection.Should().Be(
            systemSection,
            "System must stay selected exactly as the operator left it");
        shell.SelectedCatalogueSection.Should().BeNull(
            "the attempted navigation to Catalogue must not have completed");
        settings.HasUnsavedChanges.Should().BeTrue("the edit must remain intact, not discarded");
        settings.Shop.Name.Should().Be("Nimal Hardware", "nothing was reverted");
    }

    [Fact]
    public async Task UI_05_NoUnsavedChangesMeansNavigatingAwayFromSystemShowsNoDialogAtAllAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.HasUnsavedChanges.Should().BeFalse("nothing was edited yet");

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];

        dialogs.ConfirmationRequests.Should().BeEmpty(
            "a saved (or never-touched) System screen must never prompt on the way out");
        shell.SelectedCatalogueSection.Should().Be(BackOfficeShellViewModel.CatalogueSectionNames[0]);
    }

    [Fact]
    public async Task UI_05_ASavedChangeNeverPromptsOnTheWayOutAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";
        settings.HasUnsavedChanges.Should().BeTrue();

        await settings.SaveCommand.ExecuteAsync(null);
        settings.HasUnsavedChanges.Should().BeFalse("the edit was saved, not merely made");

        shell.SelectOverviewCommand.Execute(null);

        dialogs.ConfirmationRequests.Should().BeEmpty(
            "a change that was saved before navigating away must never trigger the discard guard");
        shell.IsOverviewActive.Should().BeTrue();
    }

    [Fact]
    public async Task FR_10_SaveSystemSectionCommandDoesNothingWhenADifferentNavGroupIsActiveAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, _) = await OpenAsync(fixture);
        var originalSettings = fixture.Resolve<Counterpoint.Application.Settings.ISettings>();

        // System is never selected in this test - the shell sits at Overview throughout - but the
        // real settings viewmodel underneath it can still be edited directly, exactly as it could
        // be if the operator had visited System earlier in the process lifetime and then left.
        settings.Shop.Name = "Should never reach the database";
        settings.HasUnsavedChanges.Should().BeTrue();
        shell.IsSettingsSectionActive.Should().BeFalse("Overview, not System, is the active nav group");

        shell.SaveSystemSectionCommand.Execute(null);
        await FlushAsync(settings);

        settings.HasUnsavedChanges.Should().BeTrue(
            "SaveSystemSectionCommand must no-op entirely outside System - Settings.SaveCommand must "
            + "never have been invoked");
        originalSettings.Current.Shop.Name.Should().BeEmpty(
            "nothing reached the database - the underlying SettingsViewModel.SaveCommand was never called");
    }

    [Fact]
    public async Task FR_10_RevertSystemSectionCommandDoesNothingWhenADifferentNavGroupIsActiveAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, _) = await OpenAsync(fixture);

        settings.Shop.Name = "Half-typed and forgotten";
        shell.IsSettingsSectionActive.Should().BeFalse();

        shell.RevertSystemSectionCommand.Execute(null);

        settings.Shop.Name.Should().Be(
            "Half-typed and forgotten",
            "RevertSystemSectionCommand must no-op entirely outside System - the edit must not be "
            + "thrown away by a Ctrl+R meant for a different screen");
        settings.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task FR_10_SaveSystemSectionCommandSavesTheRealSettingsWhenSystemIsActiveAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, _) = await OpenAsync(fixture);
        var liveSettings = fixture.Resolve<Counterpoint.Application.Settings.ISettings>();

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";
        shell.IsSettingsSectionActive.Should().BeTrue();

        shell.SaveSystemSectionCommand.Execute(null);
        await FlushAsync(settings);

        settings.HasUnsavedChanges.Should().BeFalse(
            "Ctrl+S, re-scoped through SaveSystemSectionCommand, must still reach "
            + "Settings.SaveCommand while System is active");
        liveSettings.Current.Shop.Name.Should().Be(
            "Nimal Hardware",
            "the save must actually reach the real ISettings, not merely flip a flag");
    }

    [Fact]
    public async Task FR_10_RevertSystemSectionCommandRevertsTheRealSettingsWhenSystemIsActiveAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, _) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";
        shell.IsSettingsSectionActive.Should().BeTrue();

        shell.RevertSystemSectionCommand.Execute(null);

        settings.Shop.Name.Should().Be(
            string.Empty,
            "Ctrl+R, re-scoped through RevertSystemSectionCommand, must still reach "
            + "Settings.RevertCommand while System is active");
        settings.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task UI_05_SwitchingBetweenSystemsOwnNineSubItemsNeverTriggersTheGuardEvenWithUnsavedChangesAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (shell, settings, dialogs) = await OpenAsync(fixture);

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[0]);
        settings.Shop.Name = "Nimal Hardware";
        settings.HasUnsavedChanges.Should().BeTrue();

        shell.SelectSettingsSectionCommand.Execute(BackOfficeShellViewModel.SettingsSectionNames[1]);

        dialogs.ConfirmationRequests.Should().BeEmpty(
            "moving between System's own nine sub-items is one screen, one save - the same "
            + "TabControl behaviour SettingsWindow always had, and it must never ask");
        shell.SelectedSettingsSection.Should().Be(BackOfficeShellViewModel.SettingsSectionNames[1]);
        settings.HasUnsavedChanges.Should().BeTrue("the edit must still be sitting there, untouched");
        settings.Shop.Name.Should().Be("Nimal Hardware");
    }

    /// <summary>
    /// Builds the real <see cref="SettingsViewModel"/> exactly as <c>SettingsScreenTests.Open</c>
    /// does, attaches it (and a <see cref="FakeDialogService"/>) to a fresh
    /// <see cref="BackOfficeShellViewModel"/>, the same two-step composition-root sequence
    /// <c>CounterpointHostBuilderExtensions</c> performs for real.
    /// </summary>
    private static Task<(BackOfficeShellViewModel Shell, SettingsViewModel Settings, FakeDialogService Dialogs)> OpenAsync(
        SaleFixture fixture)
    {
        var settings = new SettingsViewModel(
            fixture.Resolve<Counterpoint.Application.Settings.ISettings>(),
            fixture.Resolve<IBackupPassphraseStore>(),
            run => run(),
            fixture.Resolve<IReceiptTemplatePreviewService>(),
            manualBackup: null,
            fixture.Resolve<IBackupTargetCredentialStore>(),
            fixture.TryResolve<Counterpoint.Application.Abstractions.Backup.IBackupTargetConnectionTester>());

        var dialogs = new FakeDialogService();
        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());
        shell.AttachSettings(settings, dialogs);

        return Task.FromResult((shell, settings, dialogs));
    }

    /// <summary>
    /// <see cref="BackOfficeShellViewModel.SaveSystemSectionCommand"/> reaches
    /// <see cref="SettingsViewModel.SaveCommand"/>'s <c>Execute</c>, which - being the
    /// <c>IAsyncRelayCommand</c> CommunityToolkit generates for an async method - fires the save
    /// and returns immediately rather than awaiting it. This drains whatever that command started,
    /// the same way a real UI thread's next message-loop tick would.
    /// </summary>
    private static Task FlushAsync(SettingsViewModel settings) =>
        settings.SaveCommand.ExecutionTask ?? Task.CompletedTask;
}

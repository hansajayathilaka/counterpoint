using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Ui.ViewModels.Settings;

namespace Counterpoint.Ui.ViewModels.FirstRun;

/// <summary>
/// The first-run setup wizard: shop profile, currency and decimals, tax classes, the bill number
/// format, the receipt printer, the owner's password and where backups go
/// (P1-T03 "Do this" 5, SRS FR-10, FR-1.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>It collects; it does not configure.</b> Every page fills in part of one
/// <see cref="FirstRunSetupRequest"/> and the last page hands the whole thing to
/// <see cref="IFirstRunSetup.CompleteAsync"/>, which does the work in a single transaction. Close
/// the window on page four and the database is still an un-set-up database - which is the point
/// of collecting everything before writing anything.
/// </para>
/// <para>
/// The pages are the same viewmodels the settings screen uses, so the shop profile it collects
/// and the shop profile it can edit afterwards are the same code, and cannot drift apart.
/// </para>
/// </remarks>
public sealed partial class FirstRunWizardViewModel : ViewModelBase
{
    /// <summary>
    /// The owner account <c>FirstRunSeeder</c> creates, with a hash nothing can authenticate
    /// against. The wizard gives it its first password; it does not create it. The sign-in screen
    /// starts from the same name for the same reason.
    /// </summary>
    private const string SeededOwnerUsername = "owner";

    private readonly IFirstRunSetup _setup;
    private readonly ISettings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentStepHeading))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private int _stepIndex;

    [ObservableProperty]
    private string _ownerUsername = SeededOwnerUsername;

    [ObservableProperty]
    private string _ownerPassword = string.Empty;

    [ObservableProperty]
    private string _confirmOwnerPassword = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _busy;

    public FirstRunWizardViewModel(IFirstRunSetup setup, ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(settings);

        _setup = setup;
        _settings = settings;
    }

    /// <summary>
    /// Raised once the shop is set up - or was already. The composition root opens the sign-in
    /// screen; the viewmodel does not know what a window is.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>FR-10.1.</summary>
    public ShopProfileSettingsViewModel Shop { get; } = new();

    /// <summary>FR-10.2.</summary>
    public FinancialSettingsViewModel Financial { get; } = new();

    /// <summary>FR-10.3 - the default class, its rate, and the word tax is printed under.</summary>
    public TaxSettingsViewModel Tax { get; } = new();

    /// <summary>The classes to create. Each becomes a <c>tax_class</c> row.</summary>
    public ObservableCollection<TaxClassEntryViewModel> TaxClasses { get; } = [];

    /// <summary>FR-10.4 - the bill series. The other five keep their defaults until edited.</summary>
    public DocumentNumberingViewModel BillNumbering { get; } = new("Bills");

    /// <summary>FR-10.6 - the receipt printer, named rather than discovered.</summary>
    public PeripheralSettingsViewModel Peripherals { get; } = new();

    /// <summary>FR-10.7 - where backups go, and the passphrase they are encrypted with.</summary>
    public BackupSettingsViewModel Backup { get; } = new();

    /// <summary>The pages, in order.</summary>
    public IReadOnlyList<string> StepTitles { get; } =
    [
        "Shop profile",
        "Currency and rounding",
        "Tax",
        "Bill numbers",
        "Receipt printer",
        "Owner account",
        "Backups",
    ];

    /// <summary>The page being shown.</summary>
    public string CurrentStepTitle => StepTitles[StepIndex];

    /// <summary>"Step 3 of 7 - Tax", for the top of the window.</summary>
    public string CurrentStepHeading => string.Create(
        CultureInfo.CurrentCulture,
        $"Step {StepIndex + 1} of {StepTitles.Count} - {CurrentStepTitle}");

    /// <summary>True on the last page, where Next becomes Finish.</summary>
    public bool IsLastStep => StepIndex == StepTitles.Count - 1;

    /// <summary>True anywhere but the first page.</summary>
    public bool CanGoBack => StepIndex > 0;

    /// <summary>Fills the pages with the defaults the shop starts from.</summary>
    [RelayCommand]
    public void Load()
    {
        var snapshot = _settings.Current;

        Shop.Load(snapshot);
        Financial.Load(snapshot);
        Tax.Load(snapshot);
        Peripherals.Load(snapshot);
        Backup.Load(snapshot);
        BillNumbering.Load(snapshot.Numbering.Bill);

        TaxClasses.Clear();
        TaxClasses.Add(new TaxClassEntryViewModel(
            snapshot.Tax.DefaultTaxClassName,
            SettingsText.FromTaxRate(snapshot.Tax.DefaultTaxRate)));

        StepIndex = 0;
        Status = "Welcome. Seven short pages and the till is ready to trade.";
    }

    /// <summary>Moves on, if this page has what it needs.</summary>
    [RelayCommand]
    public void Next()
    {
        if (Describe(StepIndex) is { } problem)
        {
            Status = problem;
            return;
        }

        if (!IsLastStep)
        {
            StepIndex++;
            Status = string.Empty;
        }
    }

    /// <summary>Goes back a page. Nothing typed is lost.</summary>
    [RelayCommand]
    public void Back()
    {
        if (CanGoBack)
        {
            StepIndex--;
            Status = string.Empty;
        }
    }

    /// <summary>Adds an empty tax class row.</summary>
    [RelayCommand]
    public void AddTaxClass() => TaxClasses.Add(new TaxClassEntryViewModel());

    /// <summary>Removes a tax class row. The shop always keeps at least one.</summary>
    [RelayCommand]
    public void RemoveTaxClass(TaxClassEntryViewModel? entry)
    {
        if (entry is not null && TaxClasses.Count > 1)
        {
            TaxClasses.Remove(entry);
        }
    }

    /// <summary>
    /// Hands everything the pages collected to the Application layer, in one call.
    /// </summary>
    [RelayCommand]
    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        for (var step = 0; step < StepTitles.Count; step++)
        {
            if (Describe(step) is not { } problem)
            {
                continue;
            }

            StepIndex = step;
            Status = problem;
            return;
        }

        Busy = true;
        try
        {
            var configured = await _setup.CompleteAsync(BuildRequest(), cancellationToken)
                .ConfigureAwait(true);

            OwnerPassword = string.Empty;
            ConfirmOwnerPassword = string.Empty;
            Backup.ClearPassphraseEntry();

            Status = configured
                ? "The till is set up. Sign in to start trading."
                : "This till had already been set up. Nothing was changed.";

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (InvalidOperationException exception)
        {
            // The Application layer said no, in plain language. Show it; do not interpret it.
            Status = PlainLanguage(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled. The till has not been set up.";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Everything the pages collected, as the one value <see cref="IFirstRunSetup"/> takes.
    /// </summary>
    /// <remarks>
    /// Public so that a test can prove the wizard is a thin adapter: this request, handed to
    /// <see cref="IFirstRunSetup.CompleteAsync"/> directly, sets the shop up identically.
    /// </remarks>
    public FirstRunSetupRequest BuildRequest()
    {
        var snapshot = _settings.Current;

        snapshot = Shop.Apply(snapshot);
        snapshot = Financial.Apply(snapshot);
        snapshot = Tax.Apply(snapshot);
        snapshot = Peripherals.Apply(snapshot);
        snapshot = Backup.Apply(snapshot);

        snapshot = snapshot with
        {
            Numbering = snapshot.Numbering with
            {
                Bill = BillNumbering.Apply(snapshot.Numbering.Bill),
            },
        };

        return new FirstRunSetupRequest(
            snapshot,
            [.. TaxClasses.Where(entry => entry.IsComplete).Select(entry => entry.ToDefinition())],
            OwnerUsername.Trim(),
            OwnerPassword,
            Backup.NewPassphrase.Length == 0 ? null : Backup.NewPassphrase);
    }

    /// <summary>
    /// What is missing from a page, in a sentence, or null when it has everything it needs
    /// (SRS UI-06).
    /// </summary>
    private string? Describe(int step) => step switch
    {
        0 when Shop.Name.Trim().Length == 0 =>
            "The shop's name prints at the top of every bill. Please type it.",

        2 when !TaxClasses.Any(entry => entry.IsComplete) =>
            "Every product needs a tax class, even a zero-rated one. Please name at least one.",

        2 when Tax.DefaultTaxClassName.Trim().Length == 0 =>
            "Please name the tax class a new product gets when nobody chooses one.",

        3 when BillNumbering.Pattern.Trim().Length == 0 =>
            "A bill needs a number format. The usual one is {prefix}{yyyy}-{n:000000}.",

        5 when OwnerUsername.Trim().Length == 0 =>
            "Please type the username of the owner account this till was set up with.",

        5 when OwnerPassword.Length == 0 =>
            "The owner needs a password. There is no default one anywhere in this system.",

        5 when !string.Equals(OwnerPassword, ConfirmOwnerPassword, StringComparison.Ordinal) =>
            "The two passwords are not the same. Type them again.",

        6 => Backup.Validate(),

        _ => null,
    };

    /// <summary>The message without the argument name the framework appends to it.</summary>
    private static string PlainLanguage(Exception exception)
    {
        var message = exception.Message;
        var parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return parameter < 0 ? message : message[..parameter];
    }
}

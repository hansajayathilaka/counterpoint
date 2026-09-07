using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Settings.FirstRun;

/// <summary>
/// The headless first run (SRS FR-10, FR-1.3): shop profile, currency and decimals, tax classes,
/// document number formats, the owner's first password and the backup destination, applied to an
/// empty database in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>One transaction.</b> Settings, tax classes, number series, the owner's password and the
/// audit rows that record them all commit together. A wizard that fails half way leaves a
/// database that is still un-set-up, not a shop configured with somebody's second thoughts.
/// </para>
/// <para>
/// <b>Idempotent.</b> A database that has already been through this is left alone and
/// <see cref="CompleteAsync"/> returns false. The individual steps are idempotent too, so a run
/// that failed and is retried does not double anything up.
/// </para>
/// <para>
/// <b>It composes, it does not reimplement.</b> The owner account is P1-T02's
/// <see cref="IInitialOwnerSetup"/>, the settings are <see cref="SettingsService"/>, the number
/// series are <see cref="INumberSequenceConfiguration"/>. This is the order they go in, and
/// nothing else.
/// </para>
/// <para>
/// <b>Internal, because it is handed the concrete <see cref="SettingsService"/>.</b> It has to
/// be: nobody is signed in during first run, so it uses the internal <c>SaveAsAsync</c> that
/// takes the acting user as an argument rather than from the session, and that member is not on
/// <see cref="ISettings"/> - whose <c>SaveAsync</c> is owner-only and would refuse. Since
/// <see cref="SettingsService"/> became internal to keep that guard unavoidable, a public
/// constructor here could not name it. Callers see <see cref="IFirstRunSetup"/>, which is what
/// they already used.
/// </para>
/// </remarks>
internal sealed class FirstRunSetupService : IFirstRunSetup
{
    private readonly SettingsService _settings;
    private readonly ISettingStore _store;
    private readonly ITaxClassSeed _taxClasses;
    private readonly INumberSequenceConfiguration _sequences;
    private readonly IInitialOwnerSetup _initialOwner;
    private readonly IUserStore _users;
    private readonly BackupPassphraseStore _passphrases;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public FirstRunSetupService(
        SettingsService settings,
        ISettingStore store,
        ITaxClassSeed taxClasses,
        INumberSequenceConfiguration sequences,
        IInitialOwnerSetup initialOwner,
        IUserStore users,
        BackupPassphraseStore passphrases,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(taxClasses);
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(initialOwner);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(passphrases);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = settings;
        _store = store;
        _taxClasses = taxClasses;
        _sequences = sequences;
        _initialOwner = initialOwner;
        _users = users;
        _passphrases = passphrases;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        return !rows.ContainsKey(SettingKeys.SetupCompletedAt);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        FirstRunSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(request.TaxClasses);

        if (!await IsRequiredAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var ownerId = await FindOwnerAsync(request.OwnerUsername, cancellationToken).ConfigureAwait(false);
        var ownerPasswordNeeded = await _initialOwner.IsRequiredAsync(cancellationToken).ConfigureAwait(false);

        // The passphrase goes to the protected store first, so that the settings snapshot the
        // transaction writes already knows one exists. If the transaction then fails, an unused
        // passphrase is sitting in the OS store with nothing encrypted under it - inert, and the
        // next attempt overwrites it. The reverse order would leave a shop that believes it has a
        // passphrase it does not have, which is how a backup becomes unrestorable.
        if (!string.IsNullOrEmpty(request.BackupPassphrase))
        {
            _passphrases.SetInitialPassphrase(request.BackupPassphrase);
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _settings.SaveAsAsync(request.Settings, ownerId, token).ConfigureAwait(false);
                await SeedTaxClassesAsync(request, token).ConfigureAwait(false);
                await ConfigureNumberingAsync(request.Settings, token).ConfigureAwait(false);

                if (ownerPasswordNeeded)
                {
                    await _initialOwner
                        .CompleteAsync(request.OwnerUsername, request.OwnerPassword, token)
                        .ConfigureAwait(false);
                }

                await MarkCompletedAsync(ownerId, now, token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        ownerId,
                        SettingsAuditActions.FirstRunCompleted,
                        SettingsAuditActions.SettingEntityType,
                        EntityId: null,
                        AfterJson: SecurityAuditJson.Object(
                            ("shop", request.Settings.Shop.Name),
                            ("currency", request.Settings.Financial.CurrencyCode),
                            ("decimal_places", (long)request.Settings.Financial.DecimalPlaces),
                            ("tax_classes", (long)Math.Max(request.TaxClasses.Count, 1)),
                            ("backup_passphrase_set", _passphrases.HasPassphrase())),
                        Reason: "First-run setup completed on a database that had never been configured."),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        // Committed. Only now does the cache move to what the wizard chose.
        await _settings.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task SeedTaxClassesAsync(FirstRunSetupRequest request, CancellationToken token)
    {
        var definitions = request.TaxClasses.Count > 0
            ? request.TaxClasses
            : [new TaxClassDefinition(request.Settings.Tax.DefaultTaxClassName, request.Settings.Tax.DefaultTaxRate)];

        foreach (var definition in definitions)
        {
            await _taxClasses.EnsureAsync(definition.Name, definition.Rate, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lays down every series the shop issues, counter included.
    /// </summary>
    /// <remarks>
    /// <b><see cref="INumberSequenceConfiguration.InitialiseAsync"/>, not <c>ConfigureAsync</c>.</b>
    /// <c>FirstRunSeeder</c> has already created the <c>SALE</c> and <c>SHIFT</c> rows at 1 by the
    /// time the wizard is shown, and <c>ConfigureAsync</c> deliberately refuses to move a counter
    /// on an existing row - so an owner asking for bills to start at 5000 got the setting and not
    /// the counter, and the two disagreed for ever. First run is not an edit: it runs once, it is
    /// guarded by <see cref="IsRequiredAsync"/>, and it runs before any document of any kind can
    /// have been issued, so completing the seeder's row is safe here and nowhere else. The general
    /// settings screen keeps going through <c>ConfigureAsync</c> (CLAUDE.md invariant 4, FR-10.4).
    /// </remarks>
    private async Task ConfigureNumberingAsync(SettingsSnapshot settings, CancellationToken token)
    {
        foreach (var (documentType, series) in settings.Numbering.BySequence)
        {
            await _sequences
                .InitialiseAsync(documentType, series.Prefix, series.Pattern, series.StartingNumber, token)
                .ConfigureAwait(false);
        }
    }

    private Task MarkCompletedAsync(long? ownerId, DateTimeOffset now, CancellationToken token) =>
        _store.WriteAsync(
            [
                new SettingWrite(
                    SettingKeys.SetupCompletedAt,
                    now.ToString("O", CultureInfo.InvariantCulture),
                    SettingValueTypes.Text,
                    ownerId),
            ],
            token);

    private async Task<long?> FindOwnerAsync(string username, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var owners = await _users.ListActiveOwnersAsync(cancellationToken).ConfigureAwait(false);
        var trimmed = username.Trim();

        var owner = owners.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, trimmed, StringComparison.Ordinal));

        return owner?.Id
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is no active owner account called '{trimmed}' to set up."));
    }
}

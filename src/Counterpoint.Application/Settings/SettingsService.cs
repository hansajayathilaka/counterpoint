using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The shop's settings: read through a cache, written one changed key at a time, every change
/// audited (SRS FR-10, NFR-M1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The cache is one immutable reference.</b> Readers take
/// <see cref="Volatile.Read{T}(ref T)"/> of a <see cref="Loaded"/> record and hold a whole
/// consistent set of settings; no lock is taken and any number of threads may read at once. A
/// write builds the next record and publishes it with a single
/// <see cref="Volatile.Write{T}(ref T, T)"/>, so nobody ever observes half a change. Writes are
/// serialised behind a <see cref="SemaphoreSlim"/> - there is one till and one settings owner
/// (C-01), so contention is not the point; not losing a concurrent edit is.
/// </para>
/// <para>
/// <b>Publication happens after the commit, never before.</b> The new snapshot is installed only
/// once <c>ExecuteInTransactionAsync</c> has returned, so a write that rolls back leaves no audit
/// row, no changed row and no poisoned cache. This is the whole answer to the risk noted on
/// P1-T03 ("settings read at start-up and cached forever"): a change takes effect at once,
/// without a restart, and a change that failed took effect nowhere.
/// </para>
/// <para>
/// <b>It only ever touches keys it owns.</b> The diff is against the rows actually on disk, and
/// only differing keys are written - so P1-T02's <c>security.*</c> rows share the table
/// untouched, and a key missing from a fresh database is materialised on the first save rather
/// than silently left to the fallback for ever.
/// </para>
/// <para>
/// <b>Internal, because <see cref="ISettings.SaveAsync"/> is owner-only.</b> The guard lives in
/// <c>RoleAuthorisation</c>, in front of the interface; a public implementation could be
/// constructed or resolved with nothing in front of it, and the write would go through. The
/// composition root builds one here and registers only the decorated <see cref="ISettings"/>, and
/// <c>ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic</c> fails the build if
/// this class stops being internal (SRS NFR-S2, AC-17).
/// </para>
/// </remarks>
internal sealed class SettingsService : ISettings, IDisposable
{
    private readonly ISettingStore _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly IBackupPassphraseStore _passphrases;
    private readonly INumberSequenceConfiguration _sequences;
    private readonly TimeProvider _timeProvider;

    /// <summary>Serialises writes. Reads never take it.</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private Loaded? _loaded;

    public SettingsService(
        ISettingStore store,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        ISession session,
        IBackupPassphraseStore passphrases,
        INumberSequenceConfiguration sequences,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(passphrases);
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _session = session;
        _passphrases = passphrases;
        _sequences = sequences;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SettingsSnapshot Current => Require().Snapshot;

    /// <inheritdoc />
    public ShopProfileSettings Shop => Current.Shop;

    /// <inheritdoc />
    public FinancialSettings Financial => Current.Financial;

    /// <inheritdoc />
    public TaxSettings Tax => Current.Tax;

    /// <inheritdoc />
    public NumberingSettings Numbering => Current.Numbering;

    /// <inheritdoc />
    public PolicySettings Policy => Current.Policy;

    /// <inheritdoc />
    public PeripheralSettings Peripherals => Current.Peripherals;

    /// <inheritdoc />
    public BackupSettings Backup => Current.Backup;

    /// <inheritdoc />
    public ReceiptSettings Receipt => Current.Receipt;

    /// <summary>True once <see cref="LoadAsync"/> has run.</summary>
    public bool IsLoaded => Volatile.Read(ref _loaded) is not null;

    /// <inheritdoc />
    public async Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = Rebuild(stored);

        Publish(new Loaded(snapshot, stored));
        return snapshot;
    }

    /// <inheritdoc />
    public async Task<SettingsSnapshot> SaveAsync(
        SettingsSnapshot desired,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desired);
        SettingsValidation.Validate(desired);

        // Whether a passphrase exists is read from the protected store, never taken from the
        // caller: it is not a settings row and cannot be set by editing one.
        var normalised = desired with
        {
            Backup = desired.Backup with { PassphraseIsSet = _passphrases.HasPassphrase() },
        };

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = Require();
            var changes = Diff(loaded.Stored, SettingsSerializer.ToRows(normalised));

            if (changes.Count == 0)
            {
                // Nothing moved. No transaction, no audit row: an audit trail full of "the owner
                // pressed Save" is an audit trail nobody reads (FR-10.9).
                Publish(loaded with { Snapshot = normalised });
                return normalised;
            }

            var userId = _session.CurrentUser?.Id;
            await WriteAsync(loaded, normalised, changes, userId, cancellationToken).ConfigureAwait(false);

            // Only now, with the transaction committed, does the cache move.
            Publish(new Loaded(normalised, Apply(loaded.Stored, changes)));
        }
        finally
        {
            _writeGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return normalised;
    }

    /// <inheritdoc />
    public Task<SettingsSnapshot> UpdateAsync(
        Func<SettingsSnapshot, SettingsSnapshot> edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return SaveAsync(edit(Current), cancellationToken);
    }

    /// <summary>
    /// Writes the settings the first-run wizard chose, on behalf of the owner it is creating.
    /// </summary>
    /// <remarks>
    /// Nobody is signed in during first run, so the acting user cannot come from the session. The
    /// owner being set up is the only person it could be, which is the same reasoning
    /// <c>InitialOwnerSetupService</c> uses when it files the first password against the owner
    /// themselves. Internal: outside first run, the acting user is whoever is signed in and
    /// nothing may claim otherwise.
    /// </remarks>
    internal async Task<SettingsSnapshot> SaveAsAsync(
        SettingsSnapshot desired,
        long? actingUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(desired);
        SettingsValidation.Validate(desired);

        var normalised = desired with
        {
            Backup = desired.Backup with { PassphraseIsSet = _passphrases.HasPassphrase() },
        };

        var loaded = Require();
        var changes = Diff(loaded.Stored, SettingsSerializer.ToRows(normalised));

        if (changes.Count > 0)
        {
            await WriteAsync(loaded, normalised, changes, actingUserId, cancellationToken)
                .ConfigureAwait(false);
        }

        return normalised;
    }

    /// <summary>
    /// Republishes the cache from what is now on disk. Called by the first-run wizard once its
    /// transaction has committed - not before, for the same reason <see cref="SaveAsync"/> waits.
    /// </summary>
    internal Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public void Dispose() => _writeGate.Dispose();

    /// <summary>
    /// The rows and the audit trail, in one transaction. A failure anywhere in here rolls the
    /// whole thing back, and the cache is never touched from inside it.
    /// </summary>
    private Task WriteAsync(
        Loaded loaded,
        SettingsSnapshot desired,
        List<SettingRow> changes,
        long? userId,
        CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var writes = new List<SettingWrite>(changes.Count);
                foreach (var change in changes)
                {
                    writes.Add(new SettingWrite(change.Key, change.Value, change.ValueType, userId));
                }

                await _store.WriteAsync(writes, token).ConfigureAwait(false);

                var now = _timeProvider.GetLocalNow();
                foreach (var change in changes)
                {
                    loaded.Stored.TryGetValue(change.Key, out var before);

                    await _audit.RecordAsync(
                        new AuditEntry(
                            now,
                            userId,
                            SettingsAuditActions.SettingChanged,
                            SettingsAuditActions.SettingEntityType,

                            // app_setting is keyed on its key, not on an integer id, so there is
                            // no row id to file this against. The key is in the JSON instead.
                            EntityId: null,
                            BeforeJson: before is null ? null : Describe(change.Key, before.Value),
                            AfterJson: Describe(change.Key, change.Value)),
                        token).ConfigureAwait(false);
                }

                await ApplyNumberingAsync(loaded.Snapshot, desired, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Keeps <c>number_sequence</c> in step with the FR-10.4 settings. Only the series that
    /// actually changed are touched, and only their prefix and pattern - never the counter.
    /// </summary>
    private async Task ApplyNumberingAsync(
        SettingsSnapshot before,
        SettingsSnapshot after,
        CancellationToken cancellationToken)
    {
        var previous = before.Numbering.BySequence;
        var current = after.Numbering.BySequence;

        for (var i = 0; i < current.Count; i++)
        {
            if (previous[i].Value == current[i].Value)
            {
                continue;
            }

            var series = current[i].Value;
            await _sequences.ConfigureAsync(
                current[i].Key,
                series.Prefix,
                series.Pattern,
                series.StartingNumber,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The before/after payload of a settings audit row (FR-10.9).</summary>
    private static string Describe(string key, string value) =>
        SecurityAuditJson.Object(("key", key), ("value", value));

    /// <summary>Rebuilds the typed snapshot, taking the passphrase flag from the protected store.</summary>
    private SettingsSnapshot Rebuild(IReadOnlyDictionary<string, StoredSetting> stored)
    {
        var snapshot = SettingsSerializer.FromRows(stored, SettingDefaults.Snapshot);

        return snapshot with
        {
            Backup = snapshot.Backup with { PassphraseIsSet = _passphrases.HasPassphrase() },
        };
    }

    /// <summary>The rows whose text or type differs from what is on disk, or that are not there at all.</summary>
    private static List<SettingRow> Diff(
        IReadOnlyDictionary<string, StoredSetting> stored,
        IReadOnlyList<SettingRow> desired)
    {
        var changes = new List<SettingRow>();

        foreach (var row in desired)
        {
            if (stored.TryGetValue(row.Key, out var existing)
                && string.Equals(existing.Value, row.Value, StringComparison.Ordinal)
                && string.Equals(existing.ValueType, row.ValueType, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(row);
        }

        return changes;
    }

    /// <summary>What the table now holds: everything it held, with the changed rows over the top.</summary>
    private static Dictionary<string, StoredSetting> Apply(
        IReadOnlyDictionary<string, StoredSetting> stored,
        IReadOnlyList<SettingRow> changes)
    {
        var updated = new Dictionary<string, StoredSetting>(stored, StringComparer.Ordinal);

        foreach (var change in changes)
        {
            updated[change.Key] = new StoredSetting(change.Value, change.ValueType);
        }

        return updated;
    }

    private void Publish(Loaded loaded) => Volatile.Write(ref _loaded, loaded);

    private Loaded Require() =>
        Volatile.Read(ref _loaded) ?? throw new InvalidOperationException(
            "Settings have not been loaded. The composition root calls ISettings.LoadAsync() at "
            + "start-up, before anything reads a setting - see Counterpoint.App/Program.cs.");

    /// <summary>
    /// The cache: the typed settings and the rows they were built from, published together so
    /// that the diff on the next write is always against the same state the reader is seeing.
    /// </summary>
    private sealed record Loaded(
        SettingsSnapshot Snapshot,
        IReadOnlyDictionary<string, StoredSetting> Stored);
}

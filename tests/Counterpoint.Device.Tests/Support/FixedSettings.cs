using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Settings;

namespace Counterpoint.Device.Tests.Support;

/// <summary>
/// An <see cref="ISettings"/> that hands back one fixed snapshot - everything the template
/// renderer needs to be tested without the settings framework, a database, or a session.
/// </summary>
internal sealed class FixedSettings : ISettings
{
    private SettingsSnapshot _snapshot;

    internal FixedSettings(SettingsSnapshot? snapshot = null) => _snapshot = snapshot ?? SettingDefaults.Snapshot;

    public event EventHandler? Changed;

    public SettingsSnapshot Current => _snapshot;

    public ShopProfileSettings Shop => _snapshot.Shop;

    public FinancialSettings Financial => _snapshot.Financial;

    public TaxSettings Tax => _snapshot.Tax;

    public NumberingSettings Numbering => _snapshot.Numbering;

    public PolicySettings Policy => _snapshot.Policy;

    public PeripheralSettings Peripherals => _snapshot.Peripherals;

    public BackupSettings Backup => _snapshot.Backup;

    public ReceiptSettings Receipt => _snapshot.Receipt;

    public LabelSettings Label => _snapshot.Label;

    /// <summary>Replaces the snapshot in force - a test's way of "changing a setting".</summary>
    internal void Set(SettingsSnapshot snapshot)
    {
        _snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);

    public Task<SettingsSnapshot> SaveAsync(SettingsSnapshot desired, CancellationToken cancellationToken = default)
    {
        Set(desired);
        return Task.FromResult(_snapshot);
    }

    public Task<SettingsSnapshot> UpdateAsync(
        Func<SettingsSnapshot, SettingsSnapshot> edit,
        CancellationToken cancellationToken = default) =>
        SaveAsync(edit(_snapshot), cancellationToken);
}

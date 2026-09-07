using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The one-time step that gives a brand new database its first owner credential
/// (SRS FR-1.1, FR-1.3).
/// </summary>
public sealed class InitialOwnerSetupService : IInitialOwnerSetup
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public InitialOwnerSetupService(
        IUserStore users,
        IPasswordHasher hasher,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _users = users;
        _hasher = hasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        var owners = await _users.ListActiveOwnersAsync(cancellationToken).ConfigureAwait(false);

        return !owners.Any(owner => _hasher.IsUsable(owner.PasswordHash));
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var owners = await _users.ListActiveOwnersAsync(cancellationToken).ConfigureAwait(false);

        if (owners.Any(owner => _hasher.IsUsable(owner.PasswordHash)))
        {
            throw new NotAuthorisedException(
                "This shop already has an owner password. Sign in, then change it from the users screen.");
        }

        var target = owners.FirstOrDefault(owner =>
            string.Equals(owner.Username, username.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is no active owner account called '{username.Trim()}'."));

        PasswordHasher.RequirePasswordIsAcceptable(password);

        var passwordHash = _hasher.Hash(password);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _users.SetPasswordHashAsync(target.Id, passwordHash, token).ConfigureAwait(false);
                await _users.ClearFailedAttemptsAsync(target.Id, token).ConfigureAwait(false);

                // Filed against the owner themselves: at this moment there is nobody else it
                // could be, and the row is the shop's evidence of when its first credential was
                // created (SRS FR-1.6, FR-1.8).
                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        target.Id,
                        SecurityAuditActions.OwnerPasswordInitialised,
                        SecurityAuditActions.UserEntityType,
                        target.Id,
                        AfterJson: SecurityAuditJson.Object(
                            ("username", target.Username),
                            ("role", Roles.ToToken(target.Role))),
                        Reason: "First owner password set on a database that had none."),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }
}

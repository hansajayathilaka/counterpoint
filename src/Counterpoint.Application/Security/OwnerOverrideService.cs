using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Owner overrides, re-authenticated and audited (SRS FR-1.7, FR-1.6).
/// </summary>
/// <remarks>
/// <para>
/// The credential check is delegated to <see cref="IAuthenticationService.ReauthenticateAsync"/>
/// rather than reimplemented, so an owner's password gets exactly the same lockout, the same
/// backoff and the same audit trail whether it is typed into the login screen or into an override
/// prompt. An override box that did its own comparison would be a way round NFR-S9.
/// </para>
/// <para>
/// A cashier who is not signed in cannot request one. An override is an owner authorising
/// <em>somebody</em>, and the audit row names both - so there has to be a somebody.
/// </para>
/// </remarks>
public sealed class OwnerOverrideService : IOwnerOverrideService
{
    private readonly IAuthenticationService _authentication;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public OwnerOverrideService(
        IAuthenticationService authentication,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _authentication = authentication;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<OverrideToken> RequestAsync(
        OwnerOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = request.Action.Trim();
        var reason = request.Reason.Trim();

        if (action.Length == 0)
        {
            throw new InvalidOperationException("An override has to say what is being authorised.");
        }

        if (reason.Length == 0)
        {
            throw new InvalidOperationException(
                "An override needs a reason. It goes on the audit trail against both names.");
        }

        var requester = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in, so there is nobody for the owner to authorise. Sign in first.");

        var result = await _authentication
            .ReauthenticateAsync(request.OwnerUsername, request.OwnerPassword, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            await RecordRefusalAsync(requester, action, reason, result.Outcome.ToString(), cancellationToken)
                .ConfigureAwait(false);

            throw new NotAuthorisedException(result.Message);
        }

        var owner = result.User!;

        if (owner.Role != Role.Owner)
        {
            // The password was right; the account simply is not an owner. Recorded, because an
            // attempt to authorise an override with a cashier's credential is worth seeing.
            await RecordRefusalAsync(requester, action, reason, "NOT_AN_OWNER", cancellationToken)
                .ConfigureAwait(false);

            throw new NotAuthorisedException(string.Create(
                CultureInfo.CurrentCulture,
                $"{owner.DisplayName} is not an owner, so cannot authorise this. Ask the owner."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            token => _audit.RecordAsync(
                new AuditEntry(
                    now,

                    // The owner is the user the row is filed against: they are the one who
                    // authorised it. The cashier is named in the payload, so a single row answers
                    // "who allowed this, and for whom" (SRS FR-1.6).
                    owner.Id,
                    SecurityAuditActions.OwnerOverrideGranted,
                    SecurityAuditActions.UserEntityType,
                    owner.Id,
                    AfterJson: SecurityAuditJson.Object(
                        ("action", action),
                        ("requested_by_user_id", requester.Id),
                        ("requested_by", requester.Username),
                        ("granted_by_user_id", owner.Id),
                        ("granted_by", owner.Username)),
                    Reason: reason),
                token),
            cancellationToken).ConfigureAwait(false);

        return new OverrideToken(action, requester.Id, owner.Id, now);
    }

    private Task RecordRefusalAsync(
        AuthenticatedUser requester,
        string action,
        string reason,
        string outcome,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        return _unitOfWork.ExecuteInTransactionAsync(
            token => _audit.RecordAsync(
                new AuditEntry(
                    now,
                    requester.Id,
                    SecurityAuditActions.OwnerOverrideRefused,
                    SecurityAuditActions.UserEntityType,
                    requester.Id,
                    AfterJson: SecurityAuditJson.Object(
                        ("action", action),
                        ("requested_by_user_id", requester.Id),
                        ("outcome", outcome)),
                    Reason: reason),
                token),
            cancellationToken);
    }
}

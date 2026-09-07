using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Settings;

/// <summary>
/// Applies the FR-10.4 numbering settings to <c>number_sequence</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ConfigureAsync"/> never touches <c>next_val</c> on an existing row.</b> The
/// counter is set once, when the series is created, and after that it belongs to the allocator
/// alone: numbers are issued by <c>UPDATE ... RETURNING</c> inside a business transaction, never
/// reissued, and a cancelled document keeps its number. Letting a settings screen move the counter
/// would put two documents on one number, which is precisely what the gapless guarantee rules out
/// (CLAUDE.md invariant 4, AC-19).
/// </para>
/// <para>
/// <b><see cref="InitialiseAsync"/> may set it, and only during first run.</b> The seeder creates
/// <c>SALE</c> and <c>SHIFT</c> at 1 before the wizard runs, so without this the starting number
/// the owner typed into the wizard would be dropped for exactly those two series. It refuses on
/// any database that has already completed first run, so the exception cannot be borrowed by a
/// later caller: the guard is here, in the only code that can write the row, and not merely in
/// the caller's good intentions.
/// </para>
/// </remarks>
internal sealed class SqliteNumberSequenceConfiguration : INumberSequenceConfiguration
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteNumberSequenceConfiguration(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<bool> ConfigureAsync(
        string documentType,
        string prefix,
        string pattern,
        long startingNumber,
        CancellationToken cancellationToken = default) =>
        WriteAsync(documentType, prefix, pattern, startingNumber, firstRun: false, cancellationToken);

    /// <inheritdoc />
    public Task<bool> InitialiseAsync(
        string documentType,
        string prefix,
        string pattern,
        long startingNumber,
        CancellationToken cancellationToken = default) =>
        WriteAsync(documentType, prefix, pattern, startingNumber, firstRun: true, cancellationToken);

    private Task<bool> WriteAsync(
        string documentType,
        string prefix,
        string pattern,
        long startingNumber,
        bool firstRun,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (startingNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingNumber),
                startingNumber,
                "A document series starts at 1 or later; the allocator returns the value before it increments.");
        }

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                if (firstRun)
                {
                    await RequireFirstRunAsync(context, documentType, token).ConfigureAwait(false);
                }

                var existing = await context.Set<NumberSequence>()
                    .FirstOrDefaultAsync(row => row.DocType == documentType, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    context.Add(new NumberSequence
                    {
                        DocType = documentType,
                        Prefix = prefix,
                        Pattern = pattern,
                        NextVal = startingNumber,
                    });

                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }

                var counterMoves = firstRun && existing.NextVal != startingNumber;

                if (!counterMoves
                    && string.Equals(existing.Prefix, prefix, StringComparison.Ordinal)
                    && string.Equals(existing.Pattern, pattern, StringComparison.Ordinal))
                {
                    return false;
                }

                existing.Prefix = prefix;
                existing.Pattern = pattern;

                if (counterMoves)
                {
                    // First run only, and only on a database that has issued nothing: this is
                    // finishing the seed, not resetting a live counter.
                    existing.NextVal = startingNumber;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Refuses to set a counter on a database that is past its first run.
    /// </summary>
    /// <remarks>
    /// <c>setup.completed_at</c> is written in the same transaction as the wizard's numbering, so
    /// its absence is exactly the state in which no document of any type can yet have been issued -
    /// there is no owner account to issue one before it exists. Once it is there, the counter
    /// belongs to the allocator (CLAUDE.md invariant 4).
    /// </remarks>
    private static async Task RequireFirstRunAsync(
        PosDbContext context,
        string documentType,
        CancellationToken token)
    {
        var setUp = await context.Set<AppSetting>()
            .AnyAsync(row => row.Key == SettingKeys.SetupCompletedAt, token)
            .ConfigureAwait(false);

        if (setUp)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The '{documentType}' counter cannot be set: this shop has already completed its first run. A number that has been issued is never reissued (CLAUDE.md invariant 4, AC-19)."));
        }
    }
}

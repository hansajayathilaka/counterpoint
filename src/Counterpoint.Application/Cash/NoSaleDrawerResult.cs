namespace Counterpoint.Application.Cash;

/// <summary>What a no-sale drawer open produced (task P3-T01 "Do this" #5).</summary>
/// <param name="PrintJobId">The outbox row queued for the drawer-kick ticket, or null when no slip was requested.</param>
public sealed record NoSaleDrawerResult(long? PrintJobId);

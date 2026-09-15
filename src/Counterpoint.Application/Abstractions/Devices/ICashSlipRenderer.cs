namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a cash movement or a no-sale drawer open into the byte stream a receipt printer eats
/// (SRS FR-7.1, FR-7.7, task P3-T01).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="IReturnReceiptRenderer"/>: bytes in, bytes out, no I/O and no
/// device, so it is safe to call inside the cash-movement transaction (CLAUDE.md invariant 7). No
/// printer call is ever made from here or from the transaction it runs in; that is
/// <c>IReceiptPrinter</c>'s job, from the background worker.
/// </remarks>
public interface ICashSlipRenderer
{
    /// <summary>Renders one cash-in or cash-out slip.</summary>
    public byte[] RenderCashMovementSlip(CashMovementSlip slip);

    /// <summary>Renders one no-sale drawer-open ticket.</summary>
    public byte[] RenderNoSaleSlip(NoSaleSlip slip);
}

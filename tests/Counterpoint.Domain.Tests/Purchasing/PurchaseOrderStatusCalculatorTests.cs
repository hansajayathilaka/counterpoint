using System;
using System.Collections.Generic;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Purchasing;

/// <summary>
/// <see cref="PurchaseOrderStatusCalculator"/> - the pure receipt-progress rule
/// <c>Counterpoint.Application.Purchasing.PurchaseOrderService.RecomputeStatusAsync</c> applies
/// (SRS FR-4.10, task P2-T06 "Do this" #2). One test per lifecycle step, named for the
/// requirement it proves.
/// </summary>
public sealed class PurchaseOrderStatusCalculatorTests
{
    private const long PieceUom = 1;

    [Fact]
    public void FR_4_10_NoLineReceivedKeepsTheOrderSent()
    {
        var lines = new List<PurchaseOrderLineReceiptProgress>
        {
            new(Quantity.FromDecimal(100m, PieceUom), Quantity.FromDecimal(0m, PieceUom)),
            new(Quantity.FromDecimal(50m, PieceUom), Quantity.FromDecimal(0m, PieceUom)),
        };

        PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(lines).Should().Be(PurchaseOrderStatus.Sent);
    }

    [Fact]
    public void FR_4_10_PartialReceiptOfOneLineOfSeveralMarksTheOrderPartial()
    {
        var lines = new List<PurchaseOrderLineReceiptProgress>
        {
            new(Quantity.FromDecimal(100m, PieceUom), Quantity.FromDecimal(40m, PieceUom)),
            new(Quantity.FromDecimal(50m, PieceUom), Quantity.FromDecimal(0m, PieceUom)),
        };

        PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(lines).Should().Be(PurchaseOrderStatus.Partial);
    }

    [Fact]
    public void FR_4_10_OneLineFullyReceivedWhileAnotherIsUntouchedIsStillPartial()
    {
        var lines = new List<PurchaseOrderLineReceiptProgress>
        {
            new(Quantity.FromDecimal(100m, PieceUom), Quantity.FromDecimal(100m, PieceUom)),
            new(Quantity.FromDecimal(50m, PieceUom), Quantity.FromDecimal(0m, PieceUom)),
        };

        PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(lines).Should().Be(PurchaseOrderStatus.Partial);
    }

    [Fact]
    public void FR_4_10_EveryLineFullyReceivedMarksTheOrderReceived()
    {
        var lines = new List<PurchaseOrderLineReceiptProgress>
        {
            new(Quantity.FromDecimal(100m, PieceUom), Quantity.FromDecimal(100m, PieceUom)),
            new(Quantity.FromDecimal(50m, PieceUom), Quantity.FromDecimal(50m, PieceUom)),
        };

        PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(lines).Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void FR_4_10_AnOverReceivedLineStillCountsAsFullyReceived()
    {
        var lines = new List<PurchaseOrderLineReceiptProgress>
        {
            new(Quantity.FromDecimal(100m, PieceUom), Quantity.FromDecimal(110m, PieceUom)),
        };

        PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(lines).Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void FR_4_10_ASingleLineOrderWithNoLinesCannotBeDerived()
    {
        var act = () => PurchaseOrderStatusCalculator.DeriveFromReceiptProgress([]);

        act.Should().Throw<ArgumentException>();
    }
}

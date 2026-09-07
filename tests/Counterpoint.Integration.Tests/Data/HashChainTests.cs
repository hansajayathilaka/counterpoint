using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// The chain primitive itself (CLAUDE.md invariant 6, SRS NFR-S8): a published format, so it is
/// pinned rather than described.
/// </summary>
public sealed class HashChainTests
{
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void NFR_S8_TheGenesisHashIsSixtyFourZeros()
    {
        HashChain.GenesisHash.Should().Be(new string('0', 64));
        HashChain.GenesisHash.Should().HaveLength(HashChain.HashLength);
    }

    [Fact]
    public void NFR_S8_AHashIsSha256OfThePreviousHashConcatenatedWithTheCanonicalJson()
    {
        // Independently computed: sha256("0"*64 + "{}") in lowercase hex.
        HashChain.Compute(HashChain.GenesisHash, "{}")
            .Should().Be("5508d2b710e64bc470079e1b211d9c58e21011e59d0559e422345dc19d659a75");
    }

    [Fact]
    public void NFR_S8_ChangingOneFieldChangesTheHash()
    {
        var sale = SampleSale();
        var before = SaleHashChain.RowHash(HashChain.GenesisHash, sale);

        sale.Total = Money.FromDecimal(25.01m);

        SaleHashChain.RowHash(HashChain.GenesisHash, sale).Should().NotBe(before);
    }

    [Fact]
    public void NFR_S8_ChangingThePredecessorChangesTheHash()
    {
        var sale = SampleSale();

        SaleHashChain.RowHash(HashChain.GenesisHash, sale)
            .Should().NotBe(SaleHashChain.RowHash(new string('1', 64), sale));
    }

    [Fact]
    public void NFR_S8_TheCanonicalFormIsTheDeclaredFieldOrderWithScaledIntegersAndExplicitNulls()
    {
        SaleHashChain.Canonicalise(SampleSale()).Should().Be(
            """
            {"bill_no":"INV-2026-000001","sold_at":"2026-09-06T09:15:00.000+05:30","business_date":"2026-09-06","customer_id":null,"user_id":1,"shift_id":1,"subtotal":250000,"line_discount":0,"bill_discount":0,"tax":0,"rounding":0,"total":250000,"cogs":180000,"note":null}
            """,
            "the format is published: the DDL column order less id, prev_hash, row_hash and the "
            + "three cancellation columns; money as its scaled integer; timestamps exactly as the "
            + "column stores them; nulls spelled out; no whitespace");
    }

    [Fact]
    public void NFR_S8_StringsAreEscapedSoNoFieldValueCanForgeTheStructure()
    {
        new CanonicalJson()
            .Add("note", "a \"quoted\" \\ back\nslash\ttab")
            .ToString()
            .Should().Be("""{"note":"a \"quoted\" \\ back\nslash\ttab"}""");
    }

    [Fact]
    public void NFR_S8_TheCanonicalFormIsCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = SaleHashChain.Canonicalise(SampleSale());

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = SaleHashChain.Canonicalise(SampleSale());

            german.Should().Be(invariant, "a till configured for a comma decimal separator must "
                + "produce the same hash as one that is not");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static Sale SampleSale() => new()
    {
        BillNo = "INV-2026-000001",
        SoldAt = SoldAt,
        BusinessDate = "2026-09-06",
        CustomerId = null,
        UserId = 1,
        ShiftId = 1,
        Subtotal = Money.FromDecimal(25.00m),
        LineDiscount = Money.Zero,
        BillDiscount = Money.Zero,
        Tax = Money.Zero,
        Rounding = Money.Zero,
        Total = Money.FromDecimal(25.00m),
        Cogs = Money.FromDecimal(18.00m),
        Status = "COMPLETED",
        CancelledBy = null,
        CancelledAt = null,
        Note = null,
        PrevHash = HashChain.GenesisHash,
    };
}

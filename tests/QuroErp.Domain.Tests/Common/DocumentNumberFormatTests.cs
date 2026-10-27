using FluentAssertions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Common;

/// <summary>Document number formats (SD-BR-006 and the per-document numbering rules).</summary>
public class DocumentNumberFormatTests
{
    private static readonly DateOnly Day = new(2026, 10, 27);

    [Theory, Trait("Id", "SD-UT-006"), Trait("Rule", "SD-BR-006")]
    [InlineData(null, "SO261000001")]
    [InlineData("SO261000041", "SO261000042")]
    [InlineData("OLD-7", "SO261000001")]
    public void Next_SalesOrder_IsSoYymmFiveDigits(string? last, string expected)
    {
        DocumentNumberFormat.SalesOrder.Next(Day, last).Should().Be(expected);
        DocumentNumberFormat.SalesOrder.Period(Day).Should().Be("2610");
    }

    [Fact, Trait("Id", "SD-UT-006"), Trait("Rule", "SD-BR-006")]
    public void Next_SequenceOutOfDigits_ThrowsSdE006InsteadOfReusingNumbers()
    {
        var act = () => DocumentNumberFormat.SalesOrder.Next(Day, "SO261099999");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("SD-E-006");
        error.Args.Should().Equal("SO2610");
    }

    [Theory, Trait("Id", "SD-UT-106"), Trait("Rule", "SD-BR-106")]
    [InlineData(null, "DN0000001")]
    [InlineData("DN0000041", "DN0000042")]
    public void Next_SalesInvoice_IsDnSevenDigitsWithoutPeriod(string? last, string expected)
    {
        DocumentNumberFormat.SalesInvoice.Next(Day, last).Should().Be(expected);
        DocumentNumberFormat.SalesInvoice.Period(Day).Should().BeEmpty();
        DocumentNumberFormat.SalesInvoice.Period(Day.AddMonths(1)).Should().BeEmpty();
    }

    // SD-BR-206, MM-BR-017, MM-BR-106, MM-BR-208, MM-BR-308
    [Theory, Trait("Id", "CORE-007-T01")]
    [InlineData("CM")]
    [InlineData("PO")]
    [InlineData("GR")]
    [InlineData("UR")]
    [InlineData("CT")]
    public void Next_MonthlyDocuments_ArePrefixYymmFiveDigits(string prefix)
    {
        var format = new[]
        {
            DocumentNumberFormat.CreditMemo, DocumentNumberFormat.PurchaseOrder, DocumentNumberFormat.GoodsReceipt,
            DocumentNumberFormat.UnplannedReceipt, DocumentNumberFormat.StockCount,
        }.Single(f => f.Prefix == prefix);

        format.Next(Day, null).Should().Be($"{prefix}261000001");
    }

    [Fact, Trait("Id", "MM-UT-607"), Trait("Rule", "MM-BR-607")]
    public void Next_GoodsMovement_IsIdYymmTenDigits()
    {
        DocumentNumberFormat.GoodsMovement.Next(Day, "ID26100000000041").Should().Be("ID26100000000042");
    }

    [Fact, Trait("Id", "MM-UT-708"), Trait("Rule", "MM-BR-708")]
    public void Next_StockTransfer_IsTrYymmTenDigits()
    {
        DocumentNumberFormat.StockTransfer.Next(Day, null).Should().Be("TR26100000000001");
    }
}

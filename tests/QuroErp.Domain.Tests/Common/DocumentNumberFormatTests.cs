using FluentAssertions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Common;

/// <summary>Document number formats (SD-BR-006 and the per-document numbering rules).</summary>
public class DocumentNumberFormatTests
{
    private static readonly DateOnly Day = new(2026, 10, 27);

    [Theory, Trait("Id", "SD-UT-006"), Trait("Rule", "SD-BR-006")]
    [InlineData(null, "SO2610000001")]
    [InlineData("SO2610000041", "SO2610000042")]
    [InlineData("SO261000041", "SO2610000042")]
    [InlineData("SO261099999", "SO2610100000")]
    [InlineData("SO2609000041", "SO2610000001")]
    [InlineData("OLD-7", "SO2610000001")]
    public void Next_SalesOrder_IsSoYymmSixDigits_AndContinuesAfterLegacyFiveDigitNumbers(string? last, string expected)
    {
        DocumentNumberFormat.SalesOrder.Next(Day, last).Should().Be(expected);
        DocumentNumberFormat.SalesOrder.Period(Day).Should().Be("2610");
    }

    [Fact, Trait("Id", "SD-UT-006"), Trait("Rule", "SD-BR-006")]
    public void Next_SequenceOutOfDigits_ThrowsSdE006InsteadOfReusingNumbers()
    {
        var act = () => DocumentNumberFormat.SalesOrder.Next(Day, "SO2610999999");

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

    [Theory, Trait("Id", "SD-UT-206"), Trait("Rule", "SD-BR-206")]
    [InlineData(null, "CM2610000001")]
    [InlineData("CM261000041", "CM2610000042")]
    [InlineData("CM2610000041", "CM2610000042")]
    public void Next_CreditMemo_IsCmYymmSixDigits_ContinuingAfterMigratedFiveDigitNumbers(string? last, string expected)
    {
        DocumentNumberFormat.CreditMemo.Next(Day, last).Should().Be(expected);
    }

    // MM-BR-017, MM-BR-208, MM-BR-308; a 5-digit number already issued this month is continued after (SD-BR-006)
    [Theory, Trait("Id", "CORE-007-T01")]
    [InlineData("PO")]
    [InlineData("UR")]
    [InlineData("CT")]
    public void Next_MonthlyDocuments_ArePrefixYymmSixDigits(string prefix)
    {
        var format = new[]
        {
            DocumentNumberFormat.PurchaseOrder, DocumentNumberFormat.UnplannedReceipt, DocumentNumberFormat.StockCount,
        }.Single(f => f.Prefix == prefix);

        format.Next(Day, null).Should().Be($"{prefix}2610000001");
        format.Next(Day, $"{prefix}261000040").Should().Be($"{prefix}2610000041");
    }

    [Theory, Trait("Id", "MM-UT-106"), Trait("Rule", "MM-BR-106")]
    [InlineData("2026-10-27", "GR2610000011", "GR2610000012")]
    [InlineData("2026-11-02", "GR2610000012", "GR2611000001")]
    [InlineData("2026-10-27", null, "GR2610000001")]
    public void Next_GoodsReceipt_IsGrYymmSixDigitsOfTheReceiptDate_AndRestartsMonthly(string grDt, string? last, string expected)
    {
        var date = DateOnly.Parse(grDt, System.Globalization.CultureInfo.InvariantCulture);

        DocumentNumberFormat.GoodsReceipt.Next(date, last).Should().Be(expected);
    }

    [Theory, Trait("Id", "MM-UT-017"), Trait("Rule", "MM-BR-017")]
    [InlineData("2026-10-27", "PO2610000011", "PO2610000012")]
    [InlineData("2026-11-02", "PO2610000012", "PO2611000001")]
    [InlineData("2026-10-27", null, "PO2610000001")]
    public void Next_PurchaseOrder_IsPoYymmOfPoDateAndRestartsMonthly(string poDt, string? last, string expected)
    {
        var date = DateOnly.Parse(poDt, System.Globalization.CultureInfo.InvariantCulture);

        DocumentNumberFormat.PurchaseOrder.Next(date, last).Should().Be(expected);
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

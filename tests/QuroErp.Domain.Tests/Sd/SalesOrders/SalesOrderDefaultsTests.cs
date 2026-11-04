using FluentAssertions;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Tests.Sd.SalesOrders;

/// <summary>Sales order defaults, status and line files (SD-UC-002).</summary>
public class SalesOrderDefaultsTests
{
    private static readonly DateOnly SoDt = new(2026, 11, 5);

    [Theory, Trait("Id", "SD-UT-008")]
    [InlineData("NET_15", 15)]
    [InlineData("NET_30", 30)]
    [InlineData("NET_45", 45)]
    [InlineData("NET_60", 60)]
    [InlineData("COD_30", 0)]
    [InlineData("COD_CHECK", 0)]
    [InlineData("DUE_ON_RECEIPT", 0)]
    [InlineData(null, 0)]
    public void DueDate_NetTermsAddDays_OthersAreTheDocumentDate(string? terms, int days)
    {
        SalesOrderDefaults.DueDate(terms, SoDt).Should().Be(SoDt.AddDays(days));
    }

    [Fact, Trait("Id", "SD-UT-016")]
    public void NewLineQty_IsOne()
    {
        SalesOrderDefaults.Qty.Should().Be(1m);
    }

    [Theory, Trait("Id", "SD-UT-401"), Trait("Id", "SD-UT-020")]
    [InlineData(12.5, 10.0, 12.5)]
    [InlineData(null, 10.0, 10)]
    [InlineData(null, null, 0)]
    public void UnitPrice_LevelPriceThenStandardPrice(double? level, double? standard, decimal expected)
    {
        SalesOrderDefaults.UnitPrice((decimal?)level, (decimal?)standard).Should().Be(expected);
    }

    [Theory, Trait("Id", "SD-UT-405")]
    [InlineData("A", "A")]
    [InlineData(" A ", "A")]
    [InlineData("Z", null)]
    [InlineData(null, null)]
    public void DiscountCode_IsTheCustomerStatusWhenThatCodeExists(string? dcStatus, string? expected)
    {
        SalesOrderDefaults.DiscountCode(dcStatus, new Dictionary<string, decimal> { ["A"] = -10m }).Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-033"), Trait("Id", "BC-UT-542"), Trait("Id", "BC-UT-525")]
    public void TaxCode_CustomerCode_ExemptForCertificatesOrNonTaxableItems_WarnsForExpiredOnly()
    {
        var valid = TaxExemptionDefault.For([new("EX-1", "CA", SoDt.AddDays(1))], "CA", SoDt);
        var expired = TaxExemptionDefault.For([new("EX-1", "CA", SoDt.AddDays(-1))], "CA", SoDt);

        SalesOrderDefaults.TaxCode(5, 9, itemTaxable: true, null).Should().Be(new LineTaxDefault(5, null));
        SalesOrderDefaults.TaxCode(5, 9, itemTaxable: true, valid).Should().Be(new LineTaxDefault(9, null));
        SalesOrderDefaults.TaxCode(5, 9, itemTaxable: true, expired).Should().Be(new LineTaxDefault(5, "BC-W-525"));
        SalesOrderDefaults.TaxCode(5, 9, itemTaxable: false, null).Should().Be(new LineTaxDefault(9, null));
        SalesOrderDefaults.TaxCode(5, null, itemTaxable: false, valid).Should().Be(new LineTaxDefault(5, null));
    }

    [Theory, Trait("Id", "SD-UT-012")]
    [InlineData(new[] { 10.0, 5.0 }, new[] { 0.0, 0.0 }, "SO")]
    [InlineData(new[] { 10.0, 5.0 }, new[] { 10.0, 0.0 }, "RE")]
    [InlineData(new[] { 10.0, 5.0 }, new[] { 10.0, 5.0 }, "CL")]
    [InlineData(new[] { 10.0 }, new[] { 12.0 }, "CL")]
    public void Status_FromShippedQuantities(double[] ordered, double[] shipped, string expected)
    {
        SalesOrderStatus.FromShipments(ordered.Zip(shipped, (o, s) => ((decimal)o, (decimal)s))).Should().Be(expected);
    }

    [Theory, Trait("Id", "SD-UT-003")]
    [InlineData("RE")]
    [InlineData("CL")]
    public void NotEnteredStatus_IsSdE003(string status)
    {
        var act = () => SalesOrderStatus.EnsureEditable(status, "SO2611000001");

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("SO2611000001");
        SalesOrderStatus.EnsureEditable("SO", "SO2611000001");
    }

    [Fact, Trait("Id", "SD-UT-017"), Trait("Id", "SD-UT-018"), Trait("Id", "SD-UT-022")]
    public void LineFile_ReadsKeyAndQty_SkipsBlankAndDotRows_AndListsBadRows()
    {
        const string text = "﻿BOLT,2\r\n\r\n 012345678905 , 1.5\nNUT,.\nWASHER,0\nCLIP,x\nNO-COMMA\n";

        var (rows, errors) = SalesOrderLineFile.Parse(text, text.Length);

        rows.Should().Equal(new SalesOrderFileRow(1, "BOLT", 2m), new SalesOrderFileRow(3, "012345678905", 1.5m));
        errors.Select(e => (e.LineNo, e.Reason)).Should().Equal((5, "QTY"), (6, "QTY"), (7, "FORMAT"));
    }

    [Fact, Trait("Id", "SD-UT-017")]
    public void LineFile_OverOneMegabyteOrAThousandLines_IsSdE017()
    {
        var big = () => SalesOrderLineFile.Parse("BOLT,1", SalesOrderLineFile.MaxBytes + 1);
        var many = () => SalesOrderLineFile.Parse(string.Join("\n", Enumerable.Repeat("BOLT,1", 1001)), 7007);

        big.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-017");
        many.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-017");
        SalesOrderLineFile.Parse(string.Join("\n", Enumerable.Repeat("BOLT,1", 1000)), 7000).Rows.Should().HaveCount(1000);
    }
}

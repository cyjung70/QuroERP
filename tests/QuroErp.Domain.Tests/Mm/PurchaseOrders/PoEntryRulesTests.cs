using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Domain.Tests.Mm.PurchaseOrders;

public class PoEntryRulesTests
{
    private const long Inventory = 1;
    private const long Service = 2;
    private const long SetItem = 3;
    private const long Warehouse = 7;
    private const long Vat10 = 1;
    private const long NoTax = 2;

    private static readonly DateOnly PoDt = new(2026, 10, 30);
    private static readonly AmountRounder Rounder = new([new Currency("USD", 2), new Currency("KRW", 0)]);
    private static readonly PurchaseOrderSettings UsdBase = new("USD", TaxCalcMode.Document);

    private static readonly PurchaseOrderMasters Masters = new(
        new Dictionary<long, string> { [Inventory] = "INVENTORY", [Service] = "SERVICE", [SetItem] = "SET" },
        new HashSet<long> { Warehouse },
        new Dictionary<long, TaxCodeRate> { [Vat10] = new(Vat10, 10m, false), [NoTax] = new(NoTax, 0m, false) });

    private static PurchaseOrderHeader Header(string currency = "USD", decimal rate = 1m, string? incoterms = null, DateOnly? dueDt = null) =>
        new(9, PoDt, currency, rate, incoterms, dueDt, Warehouse, null, null, null);

    private static PurchaseOrderLineInput Line(
        decimal qty = 10m, decimal price = 100m, decimal dcRate = 0m, long item = Inventory, long taxCode = Vat10, DateOnly? dueDt = null,
        long? warehouse = Warehouse) =>
        new(null, item, null, qty, price, dcRate, dueDt, warehouse, taxCode);

    private static (IReadOnlyList<PurchaseOrderLine> Lines, PurchaseOrderTotals Totals) Calc(
        PurchaseOrderHeader header, string? previousCurrency = null, PurchaseOrderSettings? settings = null, params PurchaseOrderLineInput[] lines) =>
        PoEntryRules.Calculate(header, lines, previousCurrency, Masters, settings ?? UsdBase, Rounder);

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Theory, Trait("Id", "MM-UT-011")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Qty_MustBeAboveZero(string qty)
    {
        CodeOf(() => Calc(Header(), lines: Line(qty: decimal.Parse(qty, System.Globalization.CultureInfo.InvariantCulture))))
            .Should().Be(PoEntryRules.QtyCode);
    }

    [Fact, Trait("Id", "MM-UT-012")]
    public void Price_CannotBeZero_ButMayBeNegative()
    {
        CodeOf(() => Calc(Header(), lines: Line(price: 0m))).Should().Be(PoEntryRules.PriceCode);
        Calc(Header(), lines: Line(price: -5m)).Lines.Single().PoAmt.Should().Be(-50m);
    }

    [Theory, Trait("Id", "MM-UT-014")]
    [InlineData(SetItem)]
    [InlineData(99L)]
    public void Item_MustExistAndBeInventoryOrService(long item)
    {
        CodeOf(() => Calc(Header(), lines: Line(item: item))).Should().Be(PoEntryRules.ItemCode);
        Calc(Header(), lines: Line(item: Service)).Lines.Should().ContainSingle();
    }

    [Fact, Trait("Id", "MM-UT-015"), Trait("Id", "MM-UC-003-T07")]
    public void Amounts_GrossMinusDiscount_IsTheSupply_AndTaxIsComputedOnIt()
    {
        var (lines, totals) = Calc(Header(), lines: [Line(dcRate: 10m), Line(qty: 1m, price: D("33.335"), taxCode: NoTax)]);

        lines.Select(l => (l.Seq, l.GrossAmt, l.DcAmt, l.PoAmt, l.SupplyAmt, l.TaxAmt))
            .Should().Equal((1, 1000m, 100m, 900m, 900m, 90m), (2, D("33.34"), 0m, D("33.34"), D("33.34"), 0m));
        totals.Should().Be(new PurchaseOrderTotals(D("1033.34"), 100m, D("933.34"), 90m, D("1023.34"), D("1023.34")));
    }

    [Theory, Trait("Id", "MM-UC-003-T12")]
    [InlineData("USD", "999.99")]
    [InlineData("KRW", "1000")]
    public void Amounts_UseTheDocumentCurrencyDecimals(string currency, string gross)
    {
        var settings = new PurchaseOrderSettings(currency, TaxCalcMode.Document);

        Calc(Header(currency), settings: settings, lines: Line(qty: 3m, price: D("333.33"), taxCode: NoTax)).Lines.Single().GrossAmt
            .Should().Be(D(gross));
    }

    [Fact, Trait("Id", "MM-UT-032")]
    public void ForeignCurrency_TotalInBaseCurrency_UsesTheRate()
    {
        var krwBase = new PurchaseOrderSettings("KRW", TaxCalcMode.Line);

        var totals = Calc(Header("USD", D("1350.5")), settings: krwBase, lines: Line(qty: 1m, price: D("10.05"))).Totals;

        totals.TaxAmt.Should().Be(D("1.01"));
        totals.TotPoAmt.Should().Be(D("11.06"));
        totals.TotBaseAmt.Should().Be(14937m);
    }

    [Theory, Trait("Rule", "BC-BR-424"), Trait("Rule", "BC-BR-545")]
    [InlineData(RoundingMode.HalfUp, "10.06", "1.01", "11.07", 14951)] // 10.055, 1.006, 14950.5885
    [InlineData(RoundingMode.Down, "10.05", "1.00", "11.05", 14923)]   // 10.055, 1.005, 14923.5775
    public void MmRoundingMode_RoundsTheAmounts_TheTax_AndTheBaseTotal(RoundingMode mode, string gross, string tax, string total, decimal baseTotal)
    {
        var krwBase = new PurchaseOrderSettings("KRW", TaxCalcMode.Line, mode);

        var (lines, totals) = Calc(Header("USD", D("1350.55")), settings: krwBase, lines: Line(qty: 1m, price: D("10.055")));

        (lines.Single().GrossAmt, lines.Single().TaxAmt).Should().Be((D(gross), D(tax)));
        (totals.TaxAmt, totals.TotPoAmt, totals.TotBaseAmt).Should().Be((D(tax), D(total), baseTotal));
    }

    [Theory, Trait("Id", "MM-UT-025"), Trait("Id", "MM-UC-003-T08")]
    [InlineData("-1")]
    [InlineData("101")]
    public void DiscountRate_IsFrom0To100(string rate)
    {
        CodeOf(() => Calc(Header(), lines: Line(dcRate: decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture))))
            .Should().Be(PoEntryRules.DiscountRateCode);
        Calc(Header(), lines: Line(dcRate: 100m)).Lines.Single().PoAmt.Should().Be(0m);
    }

    [Fact, Trait("Id", "MM-UT-026")]
    public void DueDate_NotBeforeThePoDate_AndWarehouse_InUse()
    {
        CodeOf(() => Calc(Header(dueDt: PoDt.AddDays(-1)), lines: Line())).Should().Be(PoEntryRules.DueDateCode);
        CodeOf(() => Calc(Header(), lines: Line(dueDt: PoDt.AddDays(-1)))).Should().Be(PoEntryRules.DueDateCode);
        CodeOf(() => Calc(Header(), lines: Line(warehouse: 8))).Should().Be(PoEntryRules.NotFoundCode);
        Calc(Header(dueDt: PoDt), lines: Line(dueDt: PoDt.AddDays(7), warehouse: null)).Lines.Single().DueDt.Should().Be(PoDt.AddDays(7));
    }

    [Theory, Trait("Id", "MM-UT-027")]
    [InlineData("FOB", true)]
    [InlineData("DDP", true)]
    [InlineData("DDU", false)]
    public void Incoterms_IsAnIncoterms2020Code(string incoterms, bool valid)
    {
        var act = () => Calc(Header(incoterms: incoterms), lines: Line());

        if (valid)
        {
            act.Should().NotThrow();
        }
        else
        {
            CodeOf(() => act()).Should().Be(PoEntryRules.IncotermsCode);
        }
    }

    [Fact, Trait("Id", "MM-UT-031")]
    public void Currency_RateAboveZero_OneForBase_AndNoChangeOnceLinesExist()
    {
        CodeOf(() => Calc(Header("KRW", 0m), lines: Line())).Should().Be(PoEntryRules.CurrencyCode);
        CodeOf(() => Calc(Header("USD", 2m), lines: Line())).Should().Be(PoEntryRules.CurrencyCode);
        CodeOf(() => Calc(Header("KRW", 0.00074m), previousCurrency: "USD", lines: Line())).Should().Be(PoEntryRules.CurrencyCode);
        CodeOf(() => Calc(Header("EUR"), lines: Line())).Should().Be(AmountRounder.UnknownCurrencyCode);
    }

    [Fact, Trait("Id", "BC-UT-412")]
    public void Currency_RateHasAtMost10Decimals()
    {
        Calc(Header("KRW", D("0.0007407407")), lines: Line(price: 135000m)).Totals.TotBaseAmt.Should().Be(D("1100.00"));
        CodeOf(() => Calc(Header("KRW", D("0.00074074074")), lines: Line())).Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }

    [Fact]
    public void TaxCode_NotInUse_IsRequired()
    {
        CodeOf(() => Calc(Header(), lines: Line(taxCode: 0))).Should().Be(PoEntryRules.RequiredCode);
    }

    [Theory, Trait("Id", "MM-UT-021")]
    [InlineData(PoStatus.Draft, 0, 0)]
    [InlineData(PoStatus.Approved, 0, 1)]
    [InlineData(PoStatus.Approved, 1, 2)]
    public void Revision_CountsSavesAfterApproval(PoStatus status, int before, int after)
    {
        PoEntryRules.RevisionAfterSave(status, before).Should().Be(after);
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

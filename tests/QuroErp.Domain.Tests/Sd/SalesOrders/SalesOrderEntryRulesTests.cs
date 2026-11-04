using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Tests.Sd.SalesOrders;

/// <summary>Sales order entry checks and amounts (SD-UC-002, docs/business-rules/SD/sales-order.md, pricing.md).</summary>
public class SalesOrderEntryRulesTests
{
    private const long Item = 1;
    private const long Service = 2;
    private const long Set = 3;
    private const long Vat10 = 10;
    private const long NoTax = 11;
    private const long Included10 = 12;

    private static readonly DateOnly SoDt = new(2026, 11, 5);
    private static readonly IAmountRounder Rounder = new AmountRounder([new Currency("USD", 2), new Currency("KRW", 0)]);
    private static readonly SalesOrderSettings Settings = new("USD", TaxCalcMode.Document);

    private static readonly SalesOrderMasters Masters = new(
        new Dictionary<long, string> { [Item] = "INVENTORY", [Service] = "SERVICE", [Set] = "SET" },
        new Dictionary<string, decimal> { ["A"] = -10m, ["B"] = -33m },
        new Dictionary<long, TaxCodeRate>
        {
            [Vat10] = new(Vat10, 10m, false),
            [NoTax] = new(NoTax, 0m, false),
            [Included10] = new(Included10, 10m, true),
        });

    [Theory, Trait("Id", "SD-UT-001")]
    [InlineData(0)]
    [InlineData(-1)]
    public void QtyNotAboveZero_IsSdE001WithTheLineNumber(decimal qty)
    {
        var act = () => Calculate(Line(), Line(qty: qty));

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args.Single()).Should().Be(("SD-E-001", (object)2));
    }

    [Fact, Trait("Id", "SD-UT-002"), Trait("Id", "SD-UT-402")]
    public void PriceZero_IsSdE002_ButANegativePriceIsAllowed()
    {
        var zero = () => Calculate(Line(price: 0m));

        zero.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-002");
        Calculate(Line(price: -5m)).Lines.Single().SoAmt.Should().Be(-50m);
    }

    [Fact, Trait("Id", "SD-UT-004")]
    public void UnknownItem_IsSdE004()
    {
        var act = () => Calculate(Line(item: 99));

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args.Single()).Should().Be(("SD-E-004", (object)1));
    }

    [Fact, Trait("Id", "SD-UT-404")]
    public void UnknownDiscountCode_IsSdE404_AndAKnownCodeGivesItsRate()
    {
        var act = () => Calculate(Line(dcCd: "Z"));

        act.Should().Throw<DomainException>().Which.Args.Should().Equal(1, "Z");
        Calculate(Line(dcCd: "B")).Lines.Single().DcRate.Should().Be(-33m);
        Calculate(Line(dcCd: null)).Lines.Single().DcRate.Should().Be(0m);
    }

    [Theory, Trait("Id", "SD-UT-005")]
    [InlineData(10, 100, null, 1000)]
    [InlineData(10, 100, "A", 900)]
    [InlineData(3, 9.99, "B", 20.08)] // 29.97 × 0.67 = 20.0799
    [InlineData(1, 0.015, null, 0.02)]
    public void LineAmount_IsQtyTimesPriceTimesOnePlusRate_Rounded(decimal qty, decimal price, string? dcCd, decimal expected)
    {
        Calculate(Line(qty: qty, price: price, dcCd: dcCd)).Lines.Single().SoAmt.Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-007"), Trait("Id", "SD-UT-032"), Trait("Id", "SD-UT-034"), Trait("Rule", "BC-BR-541")]
    public void Totals_AreSupplyPlusTax_AndTheBaseTotal()
    {
        var (lines, totals) = Calculate(Line(dcCd: "A"), Line(item: Service, qty: 2m, price: 5m, tax: NoTax));

        lines.Select(l => (l.Seq, l.SoAmt, l.SupplyAmt, l.TaxAmt)).Should().Equal((1, 900m, 900m, 90m), (2, 10m, 10m, 0m));
        totals.Should().Be(new SalesOrderTotals(910m, 90m, 1000m, 1000m));
    }

    [Fact, Trait("Id", "SD-UT-032"), Trait("Rule", "BC-BR-544")]
    public void PriceIncludedTaxCode_SplitsTheLineAmountIntoSupplyAndTax()
    {
        var (lines, totals) = Calculate(Line(qty: 1m, price: 110m, tax: Included10));

        (lines[0].SoAmt, lines[0].SupplyAmt, lines[0].TaxAmt, totals.TotSoAmt).Should().Be((110m, 100m, 10m, 110m));
    }

    [Fact, Trait("Id", "SD-UT-031"), Trait("Rule", "BC-BR-413")]
    public void OtherCurrency_KeepsItsRate_AndTheBaseTotalUsesIt()
    {
        var header = Header() with { CurrencyCode = "KRW", ExchangeRate = 0.00075m };

        var (_, totals) = SalesOrderEntryRules.Calculate(header, [Line(qty: 1m, price: 10000m)], null, Masters, Settings, Rounder);

        (totals.TotSoAmt, totals.TotBaseAmt).Should().Be((11000m, 8.25m));
    }

    [Theory, Trait("Id", "SD-UT-031")]
    [InlineData("USD", 2, null)]
    [InlineData("KRW", 0, null)]
    [InlineData("KRW", 0.00075, "USD")]
    public void BadRateOrCurrencyChangeWithLines_IsSdE031(string currency, decimal rate, string? previous)
    {
        var act = () => SalesOrderEntryRules.Calculate(
            Header() with { CurrencyCode = currency, ExchangeRate = rate }, [Line()], previous, Masters, Settings, Rounder);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-031");
    }

    [Fact, Trait("Id", "SD-UT-035")]
    public void UnknownBillingFlow_IsSdE035()
    {
        var act = () => SalesOrderEntryRules.Calculate(Header() with { BillingFlow = "LATER" }, [Line()], null, Masters, Settings, Rounder);

        act.Should().Throw<DomainException>().Which.Should().Match<DomainException>(e => e.Code == "SD-E-035" && (string)e.Args[0] == "LATER");
    }

    [Theory, Trait("Id", "SD-UT-035")]
    [InlineData("SHIP_AND_BILL", "SHIP_THEN_BILL", true, true)]
    [InlineData("SHIP_AND_BILL", "SHIP_AND_BILL", true, false)]
    [InlineData("SHIP_AND_BILL", "BILL_THEN_SHIP", false, false)]
    public void BillingFlow_IsLockedOnlyWhenItChangesAfterAShipmentOrInvoice(string stored, string requested, bool documents, bool locked)
    {
        SalesOrderEntryRules.BillingFlowLocked(stored, requested, documents).Should().Be(locked);
    }

    [Theory]
    [InlineData("rep")]
    [InlineData("shipZipCode")]
    [InlineData("customerId")]
    public void RequiredHeaderFields_AreCommonE005(string field)
    {
        var header = field switch
        {
            "rep" => Header() with { Rep = " " },
            "shipZipCode" => Header() with { ShipTo = Header().ShipTo with { ShipZipCode = null } },
            _ => Header() with { CustomerId = 0 },
        };

        var act = () => SalesOrderEntryRules.Calculate(header, [Line()], null, Masters, Settings, Rounder);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "SD-UT-023")]
    public void AnotherCustomerOnASavedOrder_IsSdE023()
    {
        var act = () => SalesOrderEntryRules.EnsureSameCustomer(5, 6);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-023");
        SalesOrderEntryRules.EnsureSameCustomer(5, 5);
    }

    [Fact, Trait("Id", "SD-UT-009")]
    public void ShipTo_DiffersOnAnyShipToFieldOrFreight_IgnoringBlanksAndSpaces()
    {
        var customer = Header().ShipTo;

        SalesOrderEntryRules.ShipToDiffers(customer with { ShipAddr2 = " ", ShipName = " Blue River " }, customer with { ShipAddr2 = null, ShipName = "Blue River" })
            .Should().BeFalse();
        SalesOrderEntryRules.ShipToDiffers(customer with { ShipCity = "Austin" }, customer).Should().BeTrue();
        SalesOrderEntryRules.ShipToDiffers(customer with { FreightCharge = "PREPAID" }, customer).Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UT-010")]
    public void PriceLevelChanges_NewOrChangedLinesOnly_SkipServiceSetAndEqualPrices()
    {
        var (lines, _) = Calculate(
            Line(price: 120m) with { Id = 7 }, Line(price: 110m), Line(item: Service, price: 5m), Line(item: Set, price: 9m));
        var stored = new Dictionary<long, SalesOrderLine> { [7] = lines[0] };
        var references = new Dictionary<long, PriceLevelReference>
        {
            [Item] = new(Item, "BOLT", "INVENTORY", 100m, 105m),
            [Service] = new(Service, "FREIGHT", "SERVICE", 1m, null),
            [Set] = new(Set, "KIT", "SET", 1m, null),
        };

        var changes = SalesOrderEntryRules.PriceLevelChanges(lines, stored, "DLR", "USD", "USD", references);
        var unchanged = SalesOrderEntryRules.PriceLevelChanges([lines[0]], stored, "DLR", "USD", "USD", references);
        var noLevel = SalesOrderEntryRules.PriceLevelChanges(lines, stored, null, "USD", "USD", references);
        var otherCurrency = SalesOrderEntryRules.PriceLevelChanges(lines, stored, "DLR", "KRW", "USD", references);
        var standard = SalesOrderEntryRules.PriceLevelChanges([lines[1] with { SoPrc = 100m }], stored, "DLR", "USD", "USD", references);

        changes.Should().Equal(new PriceLevelChange(Item, "BOLT", 110m));
        (unchanged.Count, noLevel.Count, otherCurrency.Count, standard.Count).Should().Be((0, 0, 0, 0));
    }

    [Theory, Trait("Id", "SD-UT-309")]
    [InlineData(1000.0, 900, 200, 0, true)]
    [InlineData(1000.0, 900, 100, 0, false)]
    [InlineData(1000.0, 900, 300, 250, false)]
    [InlineData(1000.0, 1200, 100, 200, false)]
    [InlineData(0.0, 900, 5000, 0, false)]
    [InlineData(null, 900, 5000, 0, false)]
    public void CreditLimit_BalancePlusTheIncrease(double? limit, decimal balance, decimal total, decimal previous, bool exceeded)
    {
        SalesOrderEntryRules.CreditLimitExceeded((decimal?)limit, balance, total, previous).Should().Be(exceeded);
    }

    private static (IReadOnlyList<SalesOrderLine> Lines, SalesOrderTotals Totals) Calculate(params SalesOrderLineInput[] lines) =>
        SalesOrderEntryRules.Calculate(Header(), lines, null, Masters, Settings, Rounder);

    private static SalesOrderHeader Header() => new(
        1, SoDt, SoDt, SoDt.AddDays(30), "USD", 1m, "SHIP_AND_BILL", "DEMO_A", "NET_30", null,
        new SalesOrderShipTo("Blue River", "1 Main St", null, "Dallas", "TX", "75001", "USA", null, null, "COLLECT"), null);

    private static SalesOrderLineInput Line(long item = Item, decimal qty = 10m, decimal price = 100m, string? dcCd = null, long tax = Vat10) =>
        new(null, item, null, qty, price, dcCd, tax);
}

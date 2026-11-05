using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Domain.Tests.Sd.Shipments;

/// <summary>Shipment checks, status and the SHIP_AND_BILL invoice amounts (SD-UC-051, docs/business-rules/SD/shipment.md).</summary>
public class ShipmentRulesTests
{
    private const string ShipAndBill = "SHIP_AND_BILL";
    private const string ShipThenBill = "SHIP_THEN_BILL";
    private const string BillThenShip = "BILL_THEN_SHIP";
    private const long Customer = 7;
    private const long Item = 1;
    private const long Main = 100;
    private const long Consignment = 900;
    private const long Vat10 = 10;
    private const long NoTax = 11;

    private static readonly IAmountRounder Rounder = new AmountRounder([new Currency("USD", 2)]);
    private static readonly IReadOnlySet<long> Items = new HashSet<long> { Item };
    private static readonly IReadOnlySet<long> Warehouses = new HashSet<long> { Main, Consignment };
    private static readonly IReadOnlySet<long> TaxCodes = new HashSet<long> { Vat10, NoTax };

    private static readonly ShipmentOrderLine OrderLine = new(
        50, 5, "SO2611000001", 1, Customer, ShipAndBill, "USD", 1m, Item, "Bolt", 10m, 4m, 100m, "A", -10m, Vat10);

    private static readonly ShipmentInvoiceLine InvoiceLine = new(
        60, 6, "DN0000001", 1, Customer, "OP", BillThenShip, "USD", 1m, Item, "Bolt", 10m, 3m, 2m, 90m, null, 0m, NoTax, 51);

    [Theory, Trait("Id", "SD-UT-161")]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, 0)]
    public void QtyNotAboveZeroOrPriceZero_IsSdE161WithTheLineNumber(decimal qty, decimal price)
    {
        var act = () => Lines(ShipAndBill, Direct(qty: 1m), Direct(qty: qty, price: price));

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args.Single()).Should().Be(("SD-E-161", (object)4), "new lines are numbered after the saved ones");
    }

    [Fact, Trait("Id", "SD-UT-161"), Trait("Id", "SD-UT-173")]
    public void DirectLines_KeepTheirPriceAndTaxCode_WithoutDiscount_AndAreBilledUnlessShipThenBill()
    {
        var billed = Lines(ShipAndBill, Direct(qty: 2m, price: -5m));
        var unbilled = Lines(ShipThenBill, Direct(qty: 2m));

        billed.Single().Should().Be(new ShipmentLine(3, Item, null, Main, 2m, -5m, null, 0m, NoTax, null, null, 2m, null, null));
        unbilled.Single().BilledQty.Should().Be(0m);
    }

    [Fact, Trait("Id", "SD-UT-167"), Trait("Id", "SD-UT-172")]
    public void OrderLines_TakeThePriceDiscountAndTaxCodeOfTheOrderLine()
    {
        var line = Lines(ShipAndBill, Direct(qty: 3m, price: 1m) with { SalesOrderLineId = OrderLine.Id, TaxCodeId = NoTax }).Single();

        (line.ShipPrc, line.DcCd, line.DcRate, line.TaxCodeId, line.SalesOrderLineId, line.BilledQty).Should().Be((100m, "A", -10m, Vat10, (long?)50, 3m));
    }

    [Fact, Trait("Id", "SD-UT-175"), Trait("Id", "SD-UT-176")]
    public void InvoiceLines_ShipFromTheConsignmentWarehouse_WithTheInvoicePrice_AndAreBilled()
    {
        var line = Lines(BillThenShip, Direct(qty: 4m) with { SalesInvoiceLineId = InvoiceLine.Id, WarehouseId = Main }).Single();

        (line.WarehouseId, line.ShipPrc, line.TaxCodeId, line.SalesInvoiceLineId, line.SalesOrderLineId, line.BilledQty)
            .Should().Be((Consignment, 90m, NoTax, (long?)60, (long?)51, 4m));
    }

    [Fact]
    public void ASourceLineOfAnotherCustomer_ItemNotFound_WarehouseNotInUse_AndNoTaxCode_AreRefused()
    {
        var other = new Dictionary<long, ShipmentOrderLine> { [OrderLine.Id] = OrderLine with { CustomerId = 8 } };
        var customer = () => ShipmentRules.Lines(
            [Direct() with { SalesOrderLineId = OrderLine.Id }], 1, ShipAndBill, Customer, other, Invoices(), Items, Warehouses, TaxCodes, Consignment);
        var item = () => Lines(ShipAndBill, Direct() with { ItemId = 99 });
        var warehouse = () => Lines(ShipAndBill, Direct() with { WarehouseId = 5 });
        var tax = () => Lines(ShipAndBill, Direct() with { TaxCodeId = 0 });

        customer.Should().Throw<DomainException>().Which.Code.Should().Be("Common-E-006");
        item.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-004");
        warehouse.Should().Throw<DomainException>().Which.Args.Should().Equal(5L);
        tax.Should().Throw<DomainException>().Which.Code.Should().Be("Common-E-005");
    }

    [Fact, Trait("Id", "SD-UT-162")]
    public void OrderLineQty_IsLimitedToWhatIsLeft_AfterTheDeletedLinesGiveTheirQtyBack()
    {
        var added = Lines(ShipAndBill, Direct(qty: 4m) with { SalesOrderLineId = OrderLine.Id }, Direct(qty: 3m) with { SalesOrderLineId = OrderLine.Id });
        var deleted = Stored(1, 1m) with { SalesOrderLineId = OrderLine.Id };

        var over = () => ShipmentRules.EnsureRemaining(added, [], Orders());
        var fits = () => ShipmentRules.EnsureRemaining(added, [deleted], Orders());

        over.Should().Throw<DomainException>().Which.Args.Should().Equal("SO2611000001", 1, 6m);
        fits.Should().NotThrow();
    }

    [Fact, Trait("Id", "SD-UT-175"), Trait("Id", "SD-UT-143")]
    public void InvoiceLineQty_IsLimitedToTheUnshippedQty_OfABillThenShipInvoiceThatIsNotVoid()
    {
        var added = Lines(BillThenShip, Direct(qty: 6m) with { SalesInvoiceLineId = InvoiceLine.Id });

        var over = () => ShipmentRules.EnsureUnshipped(added, [], Invoices(), ShipmentStatus.Void);
        var fits = () => ShipmentRules.EnsureUnshipped(added, [Stored(1, 1m) with { SalesInvoiceLineId = InvoiceLine.Id }], Invoices(), ShipmentStatus.Void);
        var voided = () => ShipmentRules.EnsureUnshipped(
            added.Take(0), [], Invoices(InvoiceLine with { Status = ShipmentStatus.Void }), ShipmentStatus.Void);
        var voidedLine = () => ShipmentRules.EnsureUnshipped(
            Lines(BillThenShip, Direct(qty: 1m) with { SalesInvoiceLineId = InvoiceLine.Id }), [],
            Invoices(InvoiceLine with { Status = ShipmentStatus.Void }), ShipmentStatus.Void);

        InvoiceLine.UnshippedQty.Should().Be(5m);
        over.Should().Throw<DomainException>().Which.Args.Should().Equal("DN0000001", 1, 5m);
        fits.Should().NotThrow();
        voided.Should().NotThrow();
        voidedLine.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-175");
    }

    [Theory, Trait("Id", "SD-UT-174")]
    [InlineData(null, false, true, ShipThenBill, ShipThenBill)]
    [InlineData(null, false, true, BillThenShip, ShipAndBill)]
    [InlineData(null, true, false, ShipAndBill, BillThenShip)]
    [InlineData(ShipThenBill, false, true, ShipAndBill, ShipThenBill)]
    public void BillingFlow_IsTheCustomersForDirectLines_BillThenShipForInvoiceLines_AndStaysOnceSaved(
        string? stored, bool invoiceLines, bool directLines, string customerFlow, string expected) =>
        ShipmentRules.BillingFlowOf(stored, [], invoiceLines, directLines, customerFlow).Should().Be(expected);

    [Fact, Trait("Id", "SD-UT-174")]
    public void BillingFlow_IsTheOrders_AndABillThenShipOrderLineIsRefused()
    {
        var flow = ShipmentRules.BillingFlowOf(null, [OrderLine with { BillingFlow = ShipThenBill }], false, true, ShipAndBill);
        var billFirst = () => ShipmentRules.BillingFlowOf(null, [OrderLine with { BillingFlow = BillThenShip }], false, false, ShipAndBill);
        var mixedFlows = () => ShipmentRules.BillingFlowOf(ShipAndBill, [OrderLine with { BillingFlow = ShipThenBill }], false, false, ShipAndBill);

        flow.Should().Be(ShipThenBill);
        billFirst.Should().Throw<DomainException>().Which.Args.Should().Equal("SO2611000001");
        mixedFlows.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-035");
    }

    [Theory, Trait("Id", "SD-UT-175")]
    [InlineData(null, true, true)]
    [InlineData(BillThenShip, false, true)]
    [InlineData(ShipAndBill, true, false)]
    public void InvoiceLines_DoNotMixWithOtherLines(string? stored, bool invoiceLines, bool directLines)
    {
        var act = () => ShipmentRules.BillingFlowOf(stored, [], invoiceLines, directLines, ShipAndBill);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("SD-E-175");
    }

    [Fact, Trait("Id", "SD-UT-170")]
    public void SourceCurrencies_MustBeTheShipmentCurrency()
    {
        var act = () => ShipmentRules.EnsureCurrency("USD", ["USD", "EUR"]);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("USD", "EUR");
        ShipmentRules.EnsureCurrency("USD", ["USD"]);
    }

    [Fact, Trait("Id", "SD-UT-171")]
    public void ASavedLine_CannotChange()
    {
        var stored = Stored(2, 3m);
        var same = new ShipmentLineInput(stored.Id, Item, null, Main, 3m, 100m, Vat10, null, null, null, null);

        ShipmentRules.EnsureUnchanged(stored, same);
        var act = () => ShipmentRules.EnsureUnchanged(stored, same with { ShipQty = 4m });

        act.Should().Throw<DomainException>().Which.Args.Should().Equal(2);
    }

    [Fact, Trait("Id", "SD-UT-168"), Trait("Id", "SD-UT-177")]
    public void ABilledLine_BlocksOnlyUnderShipThenBill()
    {
        var lines = new[] { Stored(1, 3m), Stored(2, 3m) with { BilledQty = 1m } };

        var act = () => ShipmentRules.EnsureNotBilled(ShipThenBill, "SH0000001", lines);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("SH0000001", 2);
        ShipmentRules.EnsureNotBilled(BillThenShip, "SH0000001", lines);
        ShipmentRules.EnsureNotBilled(ShipAndBill, "SH0000001", lines);
    }

    [Fact, Trait("Id", "SD-UT-104"), Trait("Id", "SD-UT-167")]
    public void APaidInvoice_IsSdE104()
    {
        var act = () => ShipmentRules.EnsureInvoiceOpen("DN0000001", 100m, 40m);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("DN0000001");
        ShipmentRules.EnsureInvoiceOpen("DN0000001", 100m, 100m);
    }

    [Fact, Trait("Id", "SD-UT-163"), Trait("Id", "SD-UT-114")]
    public void AZeroCost_IsSdE114WithTheItemCode()
    {
        var act = () => ShipmentRules.EnsureCost(0m, "BOLT");

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("BOLT");
        ShipmentRules.EnsureCost(12.5m, "BOLT");
    }

    [Theory, Trait("Id", "SD-UT-166")]
    [InlineData(0, 0, "OP")]
    [InlineData(1, 0, "PB")]
    [InlineData(3, 1, "PB")]
    [InlineData(3, 2, "BL")]
    public void Status_FollowsTheBilledQty(decimal firstBilled, decimal secondBilled, string expected)
    {
        ShipmentStatus.FromBilling([(3m, firstBilled), (2m, secondBilled)]).Should().Be(expected);
        ShipmentStatus.FromBilling([]).Should().Be("OP");
    }

    [Fact, Trait("Id", "SD-UT-167"), Trait("Id", "SD-UT-109"), Trait("Id", "SD-UT-132")]
    public void TheInvoice_HasLineAmountsWithDiscount_TaxAtTheRate_AndTotalInBaseCurrency()
    {
        var rates = new Dictionary<long, TaxCodeRate> { [Vat10] = new(Vat10, 10m, false), [NoTax] = new(NoTax, 0m, false) };

        var (lines, totals) = ShipmentInvoice.Calculate(
            [new(1, 4m, 100m, -10m, Vat10), new(2, 2m, 120m, 0m, NoTax)], "USD", 1m, rates, new("USD", TaxCalcMode.Document), Rounder);

        lines.Should().Equal(new ShipmentInvoiceLineAmounts(1, 360m, 360m, 36m), new ShipmentInvoiceLineAmounts(2, 240m, 240m, 0m));
        totals.Should().Be(new ShipmentInvoiceTotals(600m, 36m, 636m, 636m));
    }

    private static IReadOnlyList<ShipmentLine> Lines(string billingFlow, params ShipmentLineInput[] added) =>
        ShipmentRules.Lines(added, 3, billingFlow, Customer, Orders(), Invoices(), Items, Warehouses, TaxCodes, Consignment);

    private static ShipmentLineInput Direct(decimal qty = 1m, decimal price = 100m) =>
        new(null, Item, null, Main, qty, price, NoTax, null, null, null, null);

    private static StoredShipmentLine Stored(int seq, decimal qty) =>
        new(seq * 10, seq, Item, null, Main, qty, 100m, null, 0m, Vat10, null, 0m, null, null);

    private static Dictionary<long, ShipmentOrderLine> Orders() => new() { [OrderLine.Id] = OrderLine };

    private static Dictionary<long, ShipmentInvoiceLine> Invoices(ShipmentInvoiceLine? line = null) =>
        new() { [InvoiceLine.Id] = line ?? InvoiceLine };
}

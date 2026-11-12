using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.SalesInvoices;

namespace QuroErp.Domain.Tests.Sd.SalesInvoices;

/// <summary>Sales invoice checks before save (SD-UC-003, docs/business-rules/SD/sales-invoice.md).</summary>
public class SalesInvoiceRulesTests
{
    private const string ShipAndBill = "SHIP_AND_BILL";
    private const string ShipThenBill = "SHIP_THEN_BILL";
    private const string BillThenShip = "BILL_THEN_SHIP";
    private const long Customer = 7;
    private const long Service = 1;
    private const long Stock = 2;
    private const long Main = 100;
    private const long Vat10 = 10;
    private const long NoTax = 11;

    private static readonly SalesInvoiceMasters Masters = new(
        new Dictionary<long, string> { [Service] = "SERVICE", [Stock] = "STOCK" },
        new Dictionary<long, string> { [Service] = "SVC-1", [Stock] = "BOLT" },
        new Dictionary<string, decimal> { ["A"] = -10m },
        new HashSet<long> { Vat10, NoTax },
        new HashSet<long> { Main },
        new Dictionary<long, long> { [Service] = NoTax });

    private static readonly InvoiceShipmentSource ShipmentLine = new(
        30, 3, "SH2611000001", 2, Customer, "OP", ShipThenBill, "USD", 1m, Stock, "Bolt", Main, 10m, 4m, 100m, "A", -10m, Vat10, 50);

    private static readonly InvoiceOrderSource OrderLine = new(
        50, 5, "SO2611000001", 1, Customer, "OP", BillThenShip, "USD", 1m, Stock, "Bolt", 10m, 3m, 80m, null, 0m, Vat10);

    private static readonly IReadOnlyDictionary<long, InvoiceShipmentSource> ShipmentLines = new Dictionary<long, InvoiceShipmentSource> { [30] = ShipmentLine };
    private static readonly IReadOnlyDictionary<long, InvoiceOrderSource> OrderLines = new Dictionary<long, InvoiceOrderSource> { [50] = OrderLine };

    [Theory, Trait("Id", "SD-UT-101")]
    [InlineData(0)]
    [InlineData(-1)]
    public void QtyNotAboveZero_IsSdE101WithTheLineNumber(decimal qty)
    {
        Action act = () => Lines(Direct(), Direct(qty: qty));

        Throws(act, "SD-E-101", 4); // new lines are numbered after the saved ones
    }

    [Fact, Trait("Id", "SD-UT-103")]
    public void PriceZero_IsSdE103_ButANegativePriceIsAllowed()
    {
        Throws(() => Lines(Direct(price: 0m)), "SD-E-103", 3);
        Lines(Direct(price: -5m)).Single().DnPrc.Should().Be(-5m);
    }

    [Fact, Trait("Id", "SD-UT-116")]
    public void DirectLineOfAStockItem_IsSdE116WithTheItemCode()
    {
        Throws(() => Lines(Direct() with { ItemId = Stock }), "SD-E-116", "BOLT");
    }

    [Fact, Trait("Id", "SD-UT-116"), Trait("Id", "SD-UT-404"), Trait("Id", "SD-UT-132")]
    public void DirectLine_TakesTheDiscountRate_TheHeaderWarehouse_AndTheItemTaxCode()
    {
        var line = Lines(Direct() with { DcCd = "A" }).Single();
        var noDiscount = Lines(Direct() with { DcCd = "" }).Single();

        line.Should().Be(new SalesInvoiceLine(3, Service, null, 1m, 100m, "A", -10m, NoTax, Main, null, null, null, null));
        (noDiscount.DcCd, noDiscount.DcRate).Should().Be(((string?)null, 0m));
        Throws(() => Lines(Direct() with { DcCd = "Z" }), "SD-E-404", 3, "Z");
    }

    [Fact, Trait("Id", "SD-UT-115"), Trait("Id", "SD-UT-406")]
    public void ShipmentLine_TakesItemPriceDiscountWarehouseAndOrderLineOfTheShipment()
    {
        var line = Lines(Direct(price: 1m) with { ItemId = 0, ShipmentLineId = 30, WarehouseId = 999 }).Single();

        line.Should().Be(new SalesInvoiceLine(3, Stock, "Bolt", 1m, 100m, "A", -10m, Vat10, Main, null, null, 30, 50));
    }

    [Fact, Trait("Id", "SD-UT-141")]
    public void OrderLine_TakesThePriceOfTheOrder_TheHeaderWarehouse_AndTheLot()
    {
        var line = Lines(Direct(price: 1m) with { ItemId = 0, SalesOrderLineId = 50, LotId = 9, LotNo = "L1", TaxCodeId = NoTax }).Single();

        line.Should().Be(new SalesInvoiceLine(3, Stock, "Bolt", 1m, 80m, null, 0m, NoTax, Main, 9, "L1", null, 50));
    }

    [Fact, Trait("Id", "SD-UT-115")]
    public void SourceLineOfAnotherCustomerOrUnknown_IsCommonE006()
    {
        var other = new Dictionary<long, InvoiceShipmentSource> { [30] = ShipmentLine with { CustomerId = 8 } };

        Throws(() => SalesInvoiceRules.Lines([Direct() with { ShipmentLineId = 30 }], 1, false, Customer, Main, other, OrderLines, Masters), "Common-E-006", 30L);
        Throws(() => Lines(Direct() with { SalesOrderLineId = 51 }), "Common-E-006", 51L);
    }

    [Fact, Trait("Id", "SD-UT-116")]
    public void UnknownItem_UnusableWarehouse_OrMissingTaxCode_AreRejected()
    {
        Throws(() => Lines(Direct() with { ItemId = 3 }), "SD-E-004", 3);
        Throws(() => Lines(Direct() with { WarehouseId = 101 }), "Common-E-006", 101L);
        Throws(() => Lines(Direct() with { TaxCodeId = 99 }), "Common-E-005", "taxCodeId");
    }

    [Fact, Trait("Id", "SD-UT-136")]
    public void InvoiceMadeByAShipment_TakesDirectLinesOnly()
    {
        Action act = () => SalesInvoiceRules.Lines([Direct(), Direct() with { ShipmentLineId = 30 }], 3, true, Customer, Main, ShipmentLines, OrderLines, Masters);

        Throws(act, "SD-E-136", 4);
        SalesInvoiceRules.Lines([Direct()], 3, true, Customer, Main, ShipmentLines, OrderLines, Masters).Should().ContainSingle();
    }

    [Fact, Trait("Id", "SD-UT-102")]
    public void ShipmentQtyAboveUnbilled_IsSdE102_AfterDeletedLinesGiveTheirQtyBack()
    {
        var added = new[] { Line(7m, shipmentLineId: 30) };
        var deleted = new[] { Stored(1, 1m, shipmentLineId: 30) };

        Throws(() => SalesInvoiceRules.EnsureUnbilled(added, [], ShipmentLines, "VO"), "SD-E-102", "SH2611000001", 2, 6m);
        SalesInvoiceRules.EnsureUnbilled(added, deleted, ShipmentLines, "VO");
    }

    [Theory, Trait("Id", "SD-UT-102")]
    [InlineData("VO", ShipThenBill)]
    [InlineData("OP", ShipAndBill)]
    public void VoidOrOtherFlowShipment_IsSdE102(string status, string flow)
    {
        var lines = new Dictionary<long, InvoiceShipmentSource> { [30] = ShipmentLine with { Status = status, BillingFlow = flow } };

        Throws(() => SalesInvoiceRules.EnsureUnbilled([Line(1m, shipmentLineId: 30)], [], lines, "VO"), "SD-E-102", "SH2611000001", 2, 6m);
    }

    [Fact, Trait("Id", "SD-UT-141")]
    public void OrderQtyAboveRemaining_OrClosedOrder_IsSdE141()
    {
        var closed = new Dictionary<long, InvoiceOrderSource> { [50] = OrderLine with { Status = "CL" } };

        Throws(() => SalesInvoiceRules.EnsureRemaining([Line(8m, orderLineId: 50)], [], OrderLines, "CL"), "SD-E-141", "SO2611000001", 1, 7m);
        Throws(() => SalesInvoiceRules.EnsureRemaining([Line(1m, orderLineId: 50)], [], closed, "CL"), "SD-E-141", "SO2611000001", 1, 7m);
        SalesInvoiceRules.EnsureRemaining([Line(8m, orderLineId: 50)], [Stored(1, 1m, orderLineId: 50)], OrderLines, "CL");
    }

    [Theory, Trait("Id", "SD-UT-141"), Trait("Id", "SD-UT-136")]
    [InlineData(null, true, false, ShipThenBill)]
    [InlineData(null, false, true, BillThenShip)]
    [InlineData(null, false, false, ShipThenBill)]
    [InlineData(ShipAndBill, true, false, ShipAndBill)]
    [InlineData(BillThenShip, false, true, BillThenShip)]
    public void BillingFlow_FollowsTheLines(string? stored, bool shipmentLines, bool orderLines, string expected)
    {
        SalesInvoiceRules.BillingFlowOf(stored, shipmentLines, orderLines).Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-141")]
    public void ShipmentAndOrderLinesMixed_IsSdE141()
    {
        Throws(() => SalesInvoiceRules.BillingFlowOf(null, true, true), "SD-E-141", "-", "-", 0m);
    }

    [Fact, Trait("Id", "SD-UT-131")]
    public void SourceInAnotherCurrency_IsSdE131()
    {
        Throws(() => SalesInvoiceRules.EnsureCurrency("USD", ["USD", "KRW"]), "SD-E-131", "USD", "KRW");
        SalesInvoiceRules.EnsureCurrency("USD", ["USD"]);
    }

    [Fact, Trait("Id", "SD-UT-104"), Trait("Id", "SD-UT-261")]
    public void PaidInvoice_IsSdE104()
    {
        Throws(() => SalesInvoiceRules.EnsureOpen("DN2611000001", 100m, 40m), "SD-E-104", "DN2611000001");
        SalesInvoiceRules.EnsureOpen("DN2611000001", 100m, 100m);
    }

    [Fact, Trait("Id", "SD-UT-107")]
    public void ChangedSavedLine_IsSdE107_AndNullMeansAsSaved()
    {
        var stored = Stored(2, 3m);
        var same = new SalesInvoiceLineInput(stored.Id, Service, "new name", 3m, null, null, null, null, null, null, null, null);

        SalesInvoiceRules.EnsureUnchanged(stored, same);
        Throws(() => SalesInvoiceRules.EnsureUnchanged(stored, same with { DnQty = 4m }), "SD-E-107", 2);
        Throws(() => SalesInvoiceRules.EnsureUnchanged(stored, same with { DnPrc = 1m }), "SD-E-107", 2);
        Throws(() => SalesInvoiceRules.EnsureUnchanged(stored, same with { TaxCodeId = Vat10 }), "SD-E-107", 2);
    }

    [Fact, Trait("Id", "SD-UT-119")]
    public void AnotherCustomer_IsSdE119()
    {
        Throws(() => SalesInvoiceRules.EnsureSameCustomer(Customer, 8), "SD-E-119");
        SalesInvoiceRules.EnsureSameCustomer(Customer, Customer);
    }

    [Fact, Trait("Id", "SD-UT-136"), Trait("Id", "SD-UT-144")]
    public void DeletingAShipmentLineOfShipAndBill_OrAShippedLine_IsRejected()
    {
        Throws(() => SalesInvoiceRules.EnsureDeletable("DN1", ShipAndBill, [Stored(1, 1m, shipmentLineId: 30)]), "SD-E-136", 1);
        Throws(() => SalesInvoiceRules.EnsureDeletable("DN1", BillThenShip, [Stored(2, 3m, orderLineId: 50) with { ShippedQty = 1m }]), "SD-E-144", "DN1", 2);
        Throws(() => SalesInvoiceRules.EnsureDeletable("DN1", BillThenShip, [Stored(2, 3m, orderLineId: 50) with { CancelledQty = 1m }]), "SD-E-144", "DN1", 2);
        SalesInvoiceRules.EnsureDeletable("DN1", ShipAndBill, [Stored(3, 1m)]);
    }

    [Fact, Trait("Id", "SD-UT-113"), Trait("Id", "SD-UT-144"), Trait("Id", "SD-UT-104")]
    public void Void_IsRejectedWhenPaid_MadeByShipment_OrShipped()
    {
        Throws(() => SalesInvoiceRules.EnsureVoidable("DN1", ShipThenBill, 10m, 5m, []), "SD-E-104", "DN1");
        Throws(() => SalesInvoiceRules.EnsureVoidable("DN1", ShipAndBill, 10m, 10m, []), "SD-E-113", "DN1");
        Throws(() => SalesInvoiceRules.EnsureVoidable("DN1", BillThenShip, 10m, 10m, [Stored(1, 3m), Stored(2, 3m) with { ShippedQty = 3m }]), "SD-E-144", "DN1", 2);
        SalesInvoiceRules.EnsureVoidable("DN1", BillThenShip, 10m, 10m, [Stored(1, 3m)]);
    }

    [Fact, Trait("Id", "SD-UT-145"), Trait("Id", "SD-UT-143")]
    public void Cancel_TakesUpToTheUnshippedQty_InLineOrder()
    {
        var lines = new[] { Stored(1, 5m) with { Id = 11, ShippedQty = 2m, CancelledQty = 1m }, Stored(2, 4m) with { Id = 12 } };

        var result = SalesInvoiceRules.CancelLines("DN1", BillThenShip, "OP", lines, [new(12, 1m), new(11, 2m), new(12, 3m)]);

        result.Select(r => (r.Line.Seq, r.Qty)).Should().Equal((1, 2m), (2, 4m));
        Throws(() => SalesInvoiceRules.CancelLines("DN1", BillThenShip, "OP", lines, [new(11, 3m)]), "SD-E-145", "DN1", 1, 2m);
        Throws(() => SalesInvoiceRules.CancelLines("DN1", BillThenShip, "OP", lines, [new(11, 0m)]), "SD-E-145", "DN1", 1, 2m);
    }

    [Fact, Trait("Id", "SD-UT-145")]
    public void Cancel_NeedsABillFirstInvoiceInUse_AndLinesOfIt()
    {
        var lines = new[] { Stored(1, 5m) with { Id = 11 } };

        Throws(() => SalesInvoiceRules.CancelLines("DN1", ShipThenBill, "OP", lines, [new(11, 1m)]), "SD-E-145", "DN1", "-", 0m);
        Throws(() => SalesInvoiceRules.CancelLines("DN1", BillThenShip, "VO", lines, [new(11, 1m)]), "SD-E-145", "DN1", "-", 0m);
        Throws(() => SalesInvoiceRules.CancelLines("DN1", BillThenShip, "OP", lines, []), "Common-E-005", "lines");
        Throws(() => SalesInvoiceRules.CancelLines("DN1", BillThenShip, "OP", lines, [new(99, 1m)]), "Common-E-006", 99L);
    }

    [Theory, Trait("Id", "SD-UT-145")]
    [InlineData(100, 0, 100)]
    [InlineData(100, -10, 90)]
    [InlineData(100, 5, 105)]
    public void CreditMemoPrice_IsThePriceAfterTheDiscount(decimal price, decimal dcRate, decimal expected)
    {
        SalesInvoiceRules.NetPrice(price, dcRate).Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-146")]
    public void AppliedCreditMemo_IsSdE146()
    {
        Throws(() => SalesInvoiceRules.EnsureCreditNotApplied("CM2611000001", -90m, -40m), "SD-E-146", "CM2611000001");
        SalesInvoiceRules.EnsureCreditNotApplied("CM2611000001", -90m, -90m);
    }

    [Theory, Trait("Id", "SD-UT-134")]
    [InlineData("VAT_EXEMPT", "BUSINESS", "123-45-67890", "Demo Co", "Demo CEO")]
    [InlineData("VAT_STANDARD", "INDIVIDUAL", "123-45-67890", "Demo Co", "Demo CEO")]
    [InlineData("VAT_STANDARD", "BUSINESS", "", "Demo Co", "Demo CEO")]
    [InlineData("VAT_ZERO", "BUSINESS", "123-45-67890", "Demo Co", null)]
    public void TaxInvoice_NeedsATaxableLine_AndABusinessWithItsDetails(string taxKind, string party, string regNo, string name, string? ceo)
    {
        Throws(() => SalesInvoiceRules.EnsureTaxInvoice("DN1", [taxKind], party, regNo, name, ceo), "SD-E-134", "DN1");
    }

    [Theory, Trait("Id", "SD-UT-134")]
    [InlineData("VAT_STANDARD")]
    [InlineData("VAT_ZERO")]
    public void TaxInvoice_PrintsForATaxableLineOfABusiness(string taxKind)
    {
        SalesInvoiceRules.EnsureTaxInvoice("DN1", ["VAT_EXEMPT", taxKind], "BUSINESS", "123-45-67890", "Demo Co", "Demo CEO");
    }

    [Theory, Trait("Id", "SD-UT-105")]
    [InlineData(false, 100, 100, "OP")]
    [InlineData(false, 100, 40, "PA")]
    [InlineData(false, 100, 0, "CL")]
    [InlineData(true, 100, 100, "VO")]
    public void Status_FollowsTheBalance(bool isVoid, decimal total, decimal balance, string expected)
    {
        SalesInvoiceStatus.Of(isVoid, total, balance).Should().Be(expected);
    }

    private static void Throws(Action act, string code, params object[] args)
    {
        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(code);
        error.Args.Should().Equal(args);
    }

    private static IReadOnlyList<SalesInvoiceLine> Lines(params SalesInvoiceLineInput[] added) =>
        SalesInvoiceRules.Lines(added, 3, false, Customer, Main, ShipmentLines, OrderLines, Masters);

    private static SalesInvoiceLineInput Direct(decimal qty = 1m, decimal price = 100m) =>
        new(null, Service, null, qty, price, null, null, null, null, null, null, null);

    private static SalesInvoiceLine Line(decimal qty, long? shipmentLineId = null, long? orderLineId = null) =>
        new(1, Stock, null, qty, 100m, null, 0m, Vat10, Main, null, null, shipmentLineId, orderLineId);

    private static StoredInvoiceLine Stored(int seq, decimal qty, long? shipmentLineId = null, long? orderLineId = null) =>
        new(seq * 10, seq, Service, null, qty, 100m, null, 0m, NoTax, Main, null, shipmentLineId, orderLineId, 0m, 0m);
}

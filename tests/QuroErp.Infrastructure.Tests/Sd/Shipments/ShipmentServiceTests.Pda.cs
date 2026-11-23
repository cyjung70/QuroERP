using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Domain.Sd.Shipments;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.Shipments;

/// <summary>
/// PDA shipment (SD-UC-054): the PDA reads the lines left to ship of a scanned order or BILL_THEN_SHIP invoice number and saves
/// through the same shipment API as the screen, so the shipment checks (SD-UC-051 tests in this class) apply to it as they are.
/// </summary>
public partial class ShipmentServiceTests
{
    [DatabaseFact, Trait("Id", "SD-UC-054-T01"), Trait("Id", "MM-UT-921")]
    public async Task Pda_ScannedOrderNumber_ReturnsItsLinesLeftToShip_WithCustomerAndItemFlags()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        await LocationAsync(m, m.Item);
        var so = await OrderAsync(m, ShipAndBill, (m.Item, 10m, 100m, 0m, m.NoTax), (m.LotItem, 4m, 50m, 0m, m.NoTax));
        await OrderAsync(m, ShipAndBill, (m.Item, 7m, 100m, 0m, m.NoTax));
        await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 3m)), "kim", None);

        var orders = await Service.OpenOrderLinesAsync(null, $" {so.SoNo.ToLowerInvariant()} ", null, None);
        var lotLines = await Service.OpenOrderLinesAsync(null, so.SoNo, m.LotItem, None);

        orders.Should().ContainSingle();
        (orders[0].SoNo, orders[0].CustomerId, orders[0].CustomerName).Should().Be((so.SoNo, (long?)m.Customer, $"Customer {m.Key}"));
        orders[0].Lines.Select(l => (l.Seq, l.RemainingQty, l.LotManaged, l.LocationManaged))
            .Should().Equal((1, 7m, false, true), (2, 4m, true, false));
        lotLines.Single().Lines.Select(l => (l.Seq, l.ItemId)).Should().Equal((2, m.LotItem));
        (await Service.OpenOrderLinesAsync(m.Customer, None)).Should().HaveCount(2, "the customer lookup still returns every order");
    }

    [DatabaseFact, Trait("Id", "SD-UC-054-T04"), Trait("Id", "MM-UT-923"), Trait("Id", "MM-UT-921")]
    public async Task Pda_UnknownOrClosedOrder_BillThenShipOrder_AnotherCustomer_AndAnItemNotOnTheOrder_AreRefused()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        var other = await MastersAsync(ShipAndBill);
        var so = await OrderAsync(m, ShipAndBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        var billFirst = await OrderAsync(m, BillThenShip, (m.Item, 5m, 100m, 0m, m.NoTax));
        var closed = await OrderAsync(m, ShipAndBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        await ExecuteAsync("UPDATE sd_sales_order SET status = 'CL' WHERE id = @Id", closed.Id);

        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(null, "SO-NONE-" + m.Key, null, None), LabelCode.UnknownCode, "SO-NONE-" + m.Key);
        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(null, closed.SoNo, null, None), ShipmentRules.ClosedOrderCode, closed.SoNo);
        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(null, billFirst.SoNo, null, None), ShipmentRules.BillThenShipOrderCode, billFirst.SoNo);
        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(other.Customer, so.SoNo, null, None), ShipmentRules.NotFoundCode, so.SoNo);
        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(null, so.SoNo, m.Service, None), LabelCode.UnknownCode, "S" + m.Key);
        await ShouldFailAsync(() => Service.OpenOrderLinesAsync(null, so.SoNo, -1, None), LabelCode.UnknownCode, "-1");
        var nothing = () => Service.OpenOrderLinesAsync(null, " ", null, None);
        (await nothing.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(ShipmentRules.RequiredCode);
        (await Service.OpenOrderLinesAsync(m.Customer, so.SoNo, m.Item, None)).Single().SoNo.Should().Be(so.SoNo);
    }

    [DatabaseFact, Trait("Id", "SD-UC-054-T01"), Trait("Id", "SD-UC-054-T04"), Trait("Id", "MM-UT-921"), Trait("Id", "MM-UT-923")]
    public async Task Pda_ScannedBillThenShipInvoice_ReturnsItsLinesLeftToShip_AndOtherInvoicesAreRefused()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(BillThenShip);
        var other = await MastersAsync(BillThenShip);
        var so = await OrderAsync(m, BillThenShip, (m.Item, 6m, 100m, 0m, m.NoTax));
        var invoice = await BillFirstInvoiceAsync(m, so.Lines[0], m.Item, 6m, 100m);
        var shipAndBill = await MastersAsync(ShipAndBill);
        await ReceiveAsync(shipAndBill.Item, shipAndBill.Main, 5m);
        var billed = await Service.CreateAsync(Request(shipAndBill, Direct(shipAndBill.Item, 1m)), "kim", None);

        var lines = await Service.UnshippedInvoiceLinesAsync(null, invoice.DnNo.ToLowerInvariant(), m.Item, None);

        lines.Select(l => (l.DnNo, l.UnshippedQty, l.CustomerId, l.CustomerName, l.LotManaged, l.LocationManaged))
            .Should().Equal((invoice.DnNo, 6m, (long?)m.Customer, $"Customer {m.Key}", false, false));
        await ShouldFailAsync(() => Service.UnshippedInvoiceLinesAsync(null, "DN-NONE-" + m.Key, null, None), LabelCode.UnknownCode, "DN-NONE-" + m.Key);
        await ShouldFailAsync(
            () => Service.UnshippedInvoiceLinesAsync(null, billed.SalesInvoiceNo, null, None), ShipmentRules.InvoiceLineCode, billed.SalesInvoiceNo!, "-", 0m);
        await ShouldFailAsync(() => Service.UnshippedInvoiceLinesAsync(other.Customer, invoice.DnNo, null, None), ShipmentRules.NotFoundCode, invoice.DnNo);
        await ShouldFailAsync(() => Service.UnshippedInvoiceLinesAsync(null, invoice.DnNo, m.LotItem, None), LabelCode.UnknownCode, "L" + m.Key);
        var nothing = () => Service.UnshippedInvoiceLinesAsync(null, null, null, None);
        (await nothing.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(ShipmentRules.RequiredCode);
    }

    private static async Task ShouldFailAsync<T>(Func<Task<T>> act, string code, params object[] args)
    {
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((code, args));
    }
}

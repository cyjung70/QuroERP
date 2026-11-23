using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.GoodsReceipts;

/// <summary>
/// PDA receipt (MM-UC-038): the PDA reads the remaining lines of a scanned PO number and saves through the same receipt API as
/// the screen, so the receipt checks (MM-UC-004 tests in this class) apply to it as they are.
/// </summary>
public partial class GoodsReceiptServiceTests
{
    [DatabaseFact, Trait("Id", "MM-UC-038-T01"), Trait("Id", "MM-UT-920")]
    public async Task Pda_ScannedPoNumber_ReturnsItsRemainingLines_WithVendorAndItemFlags()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 10m, 100m, m.Vat10), (m.LocLotItem, 4m, 50m, m.Vat10));
        await ApprovedPoAsync(m, (m.Item, 7m, 100m, m.Vat10));
        await Service.CreateAsync(Request(m, PoLine(po.Lines[0], 3m, m.Vat10)), "kim", None);

        var lines = await Service.OpenPoLinesAsync(null, $" {po.PoNo.ToLowerInvariant()} ", null, None);

        lines.Select(l => (l.PoNo, l.PoSeq, l.RemainingQty, l.VendorId, l.VendorName, l.LotManaged, l.LocationManaged)).Should().Equal(
            (po.PoNo, 1, 7m, (long?)m.Vendor, $"Vendor {m.Key}", false, false),
            (po.PoNo, 2, 4m, (long?)m.Vendor, $"Vendor {m.Key}", true, true));
        (await Service.OpenPoLinesAsync(m.Vendor, None)).Should().HaveCount(3, "the vendor lookup still returns every order");
    }

    [DatabaseFact, Trait("Id", "MM-UC-038-T02"), Trait("Id", "MM-UT-923")]
    public async Task Pda_UnknownPoNumber_AndAnItemNotOnThePo_AreRefused()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var other = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 10m, 100m, m.Vat10), (m.LotItem, 2m, 100m, m.Vat10));
        var done = await ApprovedPoAsync(m, (m.Item, 1m, 100m, m.Vat10));
        await Service.CreateAsync(Request(m, PoLine(done.Lines[0], 1m, m.Vat10)), "kim", None);

        var scanned = await Service.OpenPoLinesAsync(null, po.PoNo, m.LotItem, None);
        var unknownPo = () => Service.OpenPoLinesAsync(null, "PO-NONE-" + m.Key, null, None);
        var notOnPo = () => Service.OpenPoLinesAsync(null, po.PoNo, m.Service, None);
        var unknownItem = () => Service.OpenPoLinesAsync(null, po.PoNo, -1, None);
        var received = () => Service.OpenPoLinesAsync(null, done.PoNo, null, None);
        var otherVendor = () => Service.OpenPoLinesAsync(other.Vendor, po.PoNo, null, None);
        var nothing = () => Service.OpenPoLinesAsync(null, " ", null, None);

        scanned.Select(l => (l.PoSeq, l.ItemId)).Should().Equal((2, m.LotItem));
        await ShouldFailAsync(unknownPo, LabelCode.UnknownCode, "PO-NONE-" + m.Key);
        await ShouldFailAsync(notOnPo, LabelCode.UnknownCode, "S" + m.Key);
        await ShouldFailAsync(unknownItem, LabelCode.UnknownCode, "-1");
        await ShouldFailAsync(received, GoodsReceiptRules.PoNotReceivableCode, done.PoNo);
        await ShouldFailAsync(otherVendor, GoodsReceiptRules.PoNotReceivableCode, po.PoNo);
        (await nothing.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(GoodsReceiptRules.RequiredCode);
    }

    [DatabaseFact, Trait("Id", "MM-UC-038-T03"), Trait("Id", "MM-UT-920"), Trait("Id", "MM-UT-910"), Trait("Id", "MM-UT-353"),
     Trait("Id", "MM-UT-364"), Trait("Id", "MM-UT-713")]
    public async Task Pda_Save_ReceivesTheScannedLine_ToItsLocation_WithANewLotAndPallets()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.LocLotItem, 12m, 50m, m.Vat10));
        var line = (await Service.OpenPoLinesAsync(null, po.PoNo, m.LocLotItem, None)).Single();

        // What the PDA sends: the vendor and warehouse of the scanned line, the scanned Location, Pallet qty and Lot attributes
        var gr = await Service.CreateAsync(
            new SaveGoodsReceiptRequest(line.VendorId!.Value, GrDt, line.WarehouseId, null, null, null, null,
            [
                new(null, line.ItemId, null, line.WarehouseId, 12m, line.PoPrc, line.TaxCodeId, PurchaseOrderLineId: line.PurchaseOrderLineId,
                    LocationId: m.Shelf, SupplierLotNo: "SUP-9", ExpiryDt: new(2027, 6, 30), PalletQty: 5m),
            ], null),
            "kim", None);

        var saved = gr.Lines.Single();
        (saved.PoNo, saved.LocationCode, saved.SupplierLotNo, saved.ExpiryDt)
            .Should().Be((po.PoNo, "A1", "SUP-9", (DateOnly?)new DateOnly(2027, 6, 30)));
        saved.LotNo.Should().StartWith("261104-", "a purchase receipt makes a new Lot");
        (await PalletsAsync(saved.Id)).Select(p => (p.Qty, p.LocationId, p.LotId)).Should().Equal(
            (5m, m.Shelf, saved.LotId!.Value), (5m, m.Shelf, saved.LotId!.Value), (2m, m.Shelf, saved.LotId!.Value));
        (await OnHandAsync(m.LocLotItem, m.Main)).Should().Be(12m);
        await ShouldFailAsync(
            () => Service.OpenPoLinesAsync(null, po.PoNo, null, None), GoodsReceiptRules.PoNotReceivableCode, po.PoNo);
    }

    private static async Task ShouldFailAsync(Func<Task> act, string code, string arg)
    {
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((code, new object[] { arg }));
    }
}

using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.StockTransfers;

/// <summary>
/// PDA move (MM-UC-039, TASK-MM-061): the PDA saves through the stock transfer API without a type, so the server picks LT or TR
/// (MM-BR-922), and reads its scanned places through ScanPlaceAsync (MM-BR-923).
/// </summary>
public partial class StockTransferServiceTests
{
    [DatabaseFact, Trait("Id", "MM-UC-039-T01"), Trait("Id", "MM-UT-922"), Trait("Rule", "MM-BR-922"), Trait("Rule", "MM-BR-715")]
    public async Task Pda_InsideOneWarehouse_SavesAnLtWithTwoLines()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.LocItem, 5m, 10m);

        var dto = await Service.CreateAsync(
            Pda(m, null, new SaveStockTransferLineRequest(m.LocItem, 2m, ToLocationId: m.Shelf),
                new SaveStockTransferLineRequest(m.LocItem, 1m, ToLocationId: m.Shelf)),
            "kim", None);

        dto.Header.Should().BeEquivalentTo(new { MovType = "LT", FromWarehouseId = m.Main, ToWarehouseId = m.Main, LineCount = 2 });
        (await MovementsAsync(dto.Header.TransferNo)).Select(x => x.MovType).Should().Equal("LT", "LT", "LT", "LT");
        (await LocationOnHandAsync(m.LocItem, m.Main)).Should().BeEquivalentTo(new[] { ("*", 2m), ("A1", 3m) });
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T02"), Trait("Id", "MM-UT-922"), Trait("Rule", "MM-BR-922")]
    public async Task Pda_ToAnotherWarehouse_SavesATr()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);

        var dto = await Service.CreateAsync(Pda(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)), "kim", None);

        dto.Header.MovType.Should().Be("TR");
        (await OnHandAsync(m.Item, m.Main), await OnHandAsync(m.Item, m.Second)).Should().Be((3m, 2m));
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T03"), Trait("Id", "MM-UT-912"), Trait("Rule", "MM-BR-912")]
    public async Task Pda_APalletScan_MovesThePalletToTheScannedPlace()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 10m, 10m);
        var pallet = await PalletAsync(m, 4m);

        var from = await Service.ScanPlaceAsync(
            new StockTransferScanQuery { Value = LabelCode.ForPallet("PL" + m.Key, "I" + m.Key, "*", 4m, null), Step = "from" }, None);
        var to = await Service.ScanPlaceAsync(new StockTransferScanQuery { Value = LabelCode.ForLocation("W" + m.Key, "*"), Step = "TO" }, None);
        var dto = await Service.CreateAsync(
            new SaveStockTransferRequest(null, Day, from.WarehouseId, to.WarehouseId, null,
                [new SaveStockTransferLineRequest(from.Pallet!.ItemId, from.Pallet.Qty, PalletId: from.Pallet.Id, ToLocationId: to.LocationId)]),
            "kim", None);

        from.Should().BeEquivalentTo(new { Kind = "PL", WarehouseId = m.Main, LocationCode = "*" });
        from.Pallet.Should().BeEquivalentTo(new { Id = pallet, PalletNo = "PL" + m.Key, ItemId = m.Item, LotNo = "*", Qty = 4m });
        to.Should().BeEquivalentTo(new { Kind = "LC", WarehouseId = m.Second, LocationCode = "*", Pallet = (object?)null });
        dto.Header.MovType.Should().Be("TR");
        (await PalletPlaceAsync(pallet)).Should().Be((m.Second, "*", "ACTIVE"));
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T04"), Trait("Id", "MM-UT-715"), Trait("Rule", "MM-BR-715")]
    public async Task Pda_TheSameLocation_IsMmE715()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.LocItem, 5m, 10m);

        await ShouldFailAsync(Pda(m, m.Main, new SaveStockTransferLineRequest(m.LocItem, 1m)), StockTransferRules.InvalidCode, "LT", 1);
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T06"), Trait("Id", "MM-UT-355"), Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-355"),
     Trait("Rule", "MM-BR-356")]
    public async Task Pda_LotItem_AnUnknownLotIsMmE355_AndAShortLotIsMmE356_EvenInAllowInMonth()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.LotItem, 4m, 10m, "A1");
        await ReceiveAsync(m.LotItem, 2m, 10m, "B1", new DateOnly(2026, 11, 20));

        await ShouldFailAsync(Pda(m, m.Second, new SaveStockTransferLineRequest(m.LotItem, 1m, LotNo: "ZZ")), "MM-E-355");
        await ShouldFailAsync(Pda(m, m.Second, new SaveStockTransferLineRequest(m.LotItem, 5m, LotNo: "A1")), "MM-E-356");
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T07"), Trait("Id", "MM-UT-001"), Trait("Rule", "MM-BR-001")]
    public async Task Pda_Deny_MoreThanTheWarehouseHolds_IsMmE001()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 2m, 10m);

        await ShouldFailAsync(Pda(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)), "MM-E-001");
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T08"), Trait("Id", "MM-UT-002"), Trait("Rule", "MM-BR-002")]
    public async Task Pda_AllowInMonth_ANegativeBalanceInTheMonth_IsSaved_AndMarked()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 2m, 10m);
        await ReceiveAsync(m.Item, 2m, 10m, date: new DateOnly(2026, 11, 20));

        var dto = await Service.CreateAsync(Pda(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)), "kim", None);

        (await MovementsAsync(dto.Header.TransferNo)).Select(x => (x.WarehouseId, x.NegativeStock))
            .Should().Equal((m.Main, true), (m.Second, false));
    }

    [DatabaseFact, Trait("Id", "MM-UC-039-T09"), Trait("Id", "MM-UT-009"), Trait("Rule", "MM-BR-009")]
    public async Task Pda_Deny_APastMoveThatMakesALaterBalanceNegative_IsMmE009()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 3m, 10m);
        await IssueAsync(m.Item, m.Main, 2m, new DateOnly(2026, 11, 6));
        await ReceiveAsync(m.Item, 5m, 10m, date: new DateOnly(2026, 11, 20));

        await ShouldFailAsync(
            Pda(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)) with { TransferDt = new DateOnly(2026, 11, 5) }, "MM-E-009");
    }

    // October has no deal on its last day: the month-end balance is the one after the 20th
    [DatabaseFact, Trait("Id", "MM-UC-039-T10"), Trait("Id", "MM-UT-010"), Trait("Rule", "MM-BR-010")]
    public async Task Pda_AllowInMonth_ANegativeMonthEndBalance_IsMmE010()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m, date: new DateOnly(2026, 11, 2));

        await ShouldFailAsync(
            Pda(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)) with { TransferDt = new DateOnly(2026, 10, 20) }, "MM-E-010");
    }

    [DatabaseFact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public async Task ScanPlace_ReadsALocationLabel_AndRefusesWhatTheStepCannotUse()
    {
        var m = await MastersAsync();
        var pallet = await PalletAsync(m, 4m);
        var mainCode = (await QueryAsync<string>("SELECT warehouse_code FROM bc_warehouse WHERE id = @Id", new { Id = m.Main })).Single();
        var palletLabel = LabelCode.ForPallet("PL" + m.Key, "I" + m.Key, "*", 4m, null);

        var shelf = await Service.ScanPlaceAsync(new StockTransferScanQuery { Value = LabelCode.ForLocation(mainCode, "A1"), Step = "FROM" }, None);

        shelf.Should().BeEquivalentTo(new { Kind = "LC", WarehouseId = m.Main, LocationId = m.Shelf, LocationCode = "A1", Pallet = (object?)null });
        await ScanShouldFailAsync(palletLabel, "TO");
        await ScanShouldFailAsync("I" + m.Key, "FROM");
        await ScanShouldFailAsync(LabelCode.ForLocation(mainCode, "NOPE"), "TO");
        await ScanShouldFailAsync(LabelCode.ForPallet("PL-NONE" + m.Key, "I" + m.Key, "*", 4m, null), "FROM");
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE mm_pallet SET status = 'CLOSED' WHERE id = @Id", new { Id = pallet });
            await connection.ExecuteAsync("UPDATE bc_warehouse SET use_yn = false WHERE id = @Id", new { Id = m.Second });
        }
        await ScanShouldFailAsync(palletLabel, "FROM");
        await ScanShouldFailAsync(LabelCode.ForLocation("W" + m.Key, "*"), "TO");

        var noStep = () => Service.ScanPlaceAsync(new StockTransferScanQuery { Value = LabelCode.ForLocation(mainCode, "A1"), Step = "ITEM" }, None);
        (await noStep.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PdaMoveRules.InvalidStepCode);
        var noValue = () => Service.ScanPlaceAsync(new StockTransferScanQuery { Value = " ", Step = "FROM" }, None);
        (await noValue.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("Common-E-005");
    }

    private static SaveStockTransferRequest Pda(Masters m, long? to, params SaveStockTransferLineRequest[] lines) =>
        Request(m, to, lines) with { MovType = null };

    private async Task ScanShouldFailAsync(string value, string step)
    {
        var act = () => Service.ScanPlaceAsync(new StockTransferScanQuery { Value = value, Step = step }, None);
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args.Single()).Should().Be((LabelCode.UnknownCode, (object)value));
    }
}

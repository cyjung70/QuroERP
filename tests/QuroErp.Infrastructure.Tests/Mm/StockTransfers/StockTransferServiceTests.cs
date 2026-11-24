using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Application.Mm.StockTransfers;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.StockTransfers;

/// <summary>Own schema for the stock transfer tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class StockTransferSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "StockTransferSchema";
}

/// <summary>
/// Stock transfers on real PostgreSQL (MM-UC-018, TASK-MM-027). Without a company the base currency is USD (2 decimals). Each test
/// makes its own items: a plain one, a Lot-managed one and a Location-managed one, and a second warehouse; it receives stock in
/// the default warehouse on <see cref="Day"/> and sets the negative stock mode it needs. Today is <see cref="Today"/>.
/// </summary>
[Collection(StockTransferSchemaDefinition.Name)]
public partial class StockTransferServiceTests(AuthSchemaFixture db)
{
    private const string Deny = "DENY";
    private const string AllowInMonth = "ALLOW_IN_MONTH";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Day = new(2026, 11, 4);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private StockTransferService Service => new(
        db.Get<IStockTransferRepository>(), db.Get<IInventoryRepository>(), db.Get<IPalletRepository>(), db.Get<IWarehouseRepository>(),
        db.Get<ILocalizationRepository>(), Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(),
        db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    // Cost (3 × 10 + 1 × 20) / 4 = 12.5; 3 move for 37.50 on both sides
    [DatabaseFact, Trait("Id", "MM-UC-018-T01"), Trait("Id", "MM-UC-018-T02"), Trait("Id", "MM-UC-018-T04"), Trait("Id", "MM-UT-705"),
     Trait("Id", "MM-UT-706"), Trait("Id", "MM-UT-708"), Trait("Rule", "MM-BR-705"), Trait("Rule", "MM-BR-706"), Trait("Rule", "MM-BR-708")]
    public async Task Create_IssuesFromTheSendingWarehouse_ReceivesInTheOther_AtTheSameAmount_AndKeepsTheItemCost()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 3m, 10m);
        await ReceiveAsync(m.Item, 1m, 20m);
        var before = await ItemValueAsync(m.Item);

        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)), "kim", None);

        dto.Header.TransferNo.Should().MatchRegex(@"^TR2611\d{10}$");
        dto.Header.Should().BeEquivalentTo(new
        {
            MovType = "TR",
            TransferDt = Day,
            FromWarehouseId = m.Main,
            ToWarehouseId = m.Second,
            LineCount = 1,
            Amt = 37.5m,
            Remarks = "Move",
        });
        dto.Lines.Select(l => (l.Seq, l.ItemId, l.Qty, l.LotNo, l.FromLocationCode, l.ToLocationCode, l.Prc, l.Amt, l.FromOnHandQty))
            .Should().Equal((1, m.Item, 3m, "*", "*", "*", 12.5m, 37.5m, 1m));
        (await MovementsAsync(dto.Header.TransferNo)).Should().Equal(
            ("TR", m.Main, -3m, -37.5m, false), ("TR", m.Second, 3m, 37.5m, false));
        (await OnHandAsync(m.Item, m.Main), await OnHandAsync(m.Item, m.Second)).Should().Be((1m, 3m));
        (await ItemValueAsync(m.Item)).Should().Be(before, "a transfer changes neither the item's stock value nor its cost");
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T03"), Trait("Id", "MM-UT-705"), Trait("Rule", "MM-BR-705"), Trait("Rule", "MM-BR-008")]
    public async Task SameWarehouse_NoQty_AServiceItem_AndTheConsignmentWarehouse_AreRefused()
    {
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);

        await ShouldFailAsync(Request(m, m.Main, new SaveStockTransferLineRequest(m.Item, 1m)), StockTransferRules.LineCode, 1, "I" + m.Key);
        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 0m)), StockTransferRules.LineCode, 1, "I" + m.Key);
        await ShouldFailAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m), new SaveStockTransferLineRequest(m.Service, 1m)),
            StockTransferRules.LineCode, 2, "S" + m.Key);
        await ShouldFailAsync(Request(m, m.Consignment, new SaveStockTransferLineRequest(m.Item, 1m)), "MM-E-710", m.ConsignmentCode);
        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m)) with { MovType = "GI" },
            StockTransferRules.InvalidCode, "GI", 0);
        (await CountAsync("SELECT count(*) FROM mm_stock_transfer WHERE from_warehouse_id = @Id OR to_warehouse_id = @Id", m.Second)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T05"), Trait("Id", "MM-UT-707"), Trait("Rule", "MM-BR-707"), Trait("Rule", "MM-BR-601")]
    public async Task Delete_ReversesBothMovements_AndAStaleDeleteIs409()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);
        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)), "kim", None);

        var stale = () => Service.DeleteAsync(dto.Header.Id, dto.Header.UpdatedAt.AddSeconds(-1), "kim", None);
        await stale.Should().ThrowAsync<ConcurrencyException>();
        await Service.DeleteAsync(dto.Header.Id, dto.Header.UpdatedAt, "kim", None);

        (await ReversalsAsync(dto.Header.TransferNo)).Should().Equal(("TR", m.Main, 2m, Today), ("TR", m.Second, -2m, Today));
        (await OnHandAsync(m.Item, m.Main), await OnHandAsync(m.Item, m.Second)).Should().Be((5m, 0m));
        (await CountAsync("SELECT count(*) FROM mm_stock_transfer WHERE id = @Id", dto.Header.Id)).Should().Be(0);
        var gone = () => Service.GetAsync(dto.Header.Id, None);
        await gone.Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T05"), Trait("Id", "MM-UT-707"), Trait("Rule", "MM-BR-707"), Trait("Rule", "MM-BR-001")]
    public async Task Update_ReversesTheOldLines_PostsTheNewOnes_UnderTheSameNumber_AndChecksTheReceivingSide()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);
        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)), "kim", None);

        var updated = await Service.UpdateAsync(
            dto.Header.Id,
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m)) with { UpdatedAt = dto.Header.UpdatedAt, Remarks = "More" },
            "kim", None);

        updated.Header.TransferNo.Should().Be(dto.Header.TransferNo);
        (updated.Header.Remarks, updated.Lines.Single().Qty).Should().Be(("More", 1m));
        (await OnHandAsync(m.Item, m.Main), await OnHandAsync(m.Item, m.Second)).Should().Be((4m, 1m));
        (await MovementsAsync(dto.Header.TransferNo)).Should().HaveCount(4, "the first pair stays, reversed, and the new pair is added");
        (await ReversalsAsync(dto.Header.TransferNo)).Select(r => r.DocumentDt).Should().OnlyContain(d => d == Day, "an edit reverses on the transfer date");
        var stale = () => Service.UpdateAsync(dto.Header.Id, Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m)) with
        {
            UpdatedAt = dto.Header.UpdatedAt,
        }, "kim", None);
        await stale.Should().ThrowAsync<ConcurrencyException>();

        // The receiving warehouse issued the 1 it got since, so taking the transfer back would leave it at −1 (MM-BR-707)
        await IssueAsync(m.Item, m.Second, 1m);
        var undo = () => Service.DeleteAsync(dto.Header.Id, updated.Header.UpdatedAt, "kim", None);
        (await undo.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-001");
    }

    // MM-BR-601 edit: the reversal takes the transfer date, so raising the qty of a past transfer only checks the new content.
    // Dated today, the old issue would stay on the 5th until the 10th and the 5th..9th would read 5 − 2 − 4 = −1 (MM-E-009)
    [DatabaseFact, Trait("Id", "MM-UC-018-T05"), Trait("Id", "MM-UT-707"), Trait("Id", "MM-UT-601"), Trait("Rule", "MM-BR-707"),
     Trait("Rule", "MM-BR-601"), Trait("Rule", "MM-BR-009")]
    public async Task Deny_RaisingThePastQty_NetsOnTheTransferDate_EvenAfterTheReceivingSideUsedTheStock()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);
        var fifth = new DateOnly(2026, 11, 5);
        var dto = await Service.CreateAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)) with { TransferDt = fifth }, "kim", None);
        // The receiving side issued what it got on the 6th: reversing its receipt before the new one is in would read −2 there
        await IssueAsync(m.Item, m.Second, 2m, new DateOnly(2026, 11, 6));

        var updated = await Service.UpdateAsync(
            dto.Header.Id,
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 4m)) with { TransferDt = fifth, UpdatedAt = dto.Header.UpdatedAt },
            "kim", None);

        updated.Lines.Single().Qty.Should().Be(4m);
        (await ReversalsAsync(dto.Header.TransferNo)).Should().Equal(("TR", m.Main, 2m, fifth), ("TR", m.Second, -2m, fifth));
        (await OnHandAsync(m.Item, m.Main), await OnHandAsync(m.Item, m.Second)).Should().Be((1m, 2m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-004"), Trait("Rule", "MM-BR-004"), Trait("Rule", "MM-BR-707")]
    public async Task ATransferOfAClosedMonth_CannotBeChangedOrDeleted_MmE002()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m, date: new DateOnly(2026, 10, 1));
        var dto = await Service.CreateAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)) with { TransferDt = new DateOnly(2026, 10, 20) }, "kim", None);
        // close_year_month is the first open month, so 202611 closes October.
        // The test schema has no company row until one is set up; this class owns its schema, so the row goes again afterwards
        var added = (await QueryAsync<int>("SELECT count(*)::int FROM bc_company", new { })).Single() == 0;
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
                SELECT 'Quro', 'US', 'USD', 'en', 'UTC', 't', 't' WHERE NOT EXISTS (SELECT 1 FROM bc_company);
                UPDATE bc_company SET close_year_month = '202611';
                """);
        }
        try
        {
            var change = () => Service.UpdateAsync(dto.Header.Id, Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m)) with
            {
                TransferDt = new DateOnly(2026, 11, 5),
                UpdatedAt = dto.Header.UpdatedAt,
            }, "kim", None);
            (await change.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-002");
            var delete = () => Service.DeleteAsync(dto.Header.Id, dto.Header.UpdatedAt, "kim", None);
            (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-002");
        }
        finally
        {
            await using var connection = new NpgsqlConnection(db.ConnectionString);
            await connection.ExecuteAsync(added ? "DELETE FROM bc_company" : "UPDATE bc_company SET close_year_month = NULL");
        }
        (await OnHandAsync(m.Item, m.Second)).Should().Be(2m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T07"), Trait("Id", "MM-UT-357"), Trait("Id", "MM-UT-355"), Trait("Rule", "MM-BR-357"),
     Trait("Rule", "MM-BR-355"), Trait("Rule", "MM-BR-362")]
    public async Task LotItem_MovesTheLotItTakes_ToTheSameLot_AndAnUnknownLotIsMmE355()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.LotItem, 4m, 10m, "A1");
        await ReceiveAsync(m.LotItem, 6m, 10m, "B1");

        var dto = await Service.CreateAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.LotItem, 5m, LotNo: "B1")), "kim", None);

        dto.Lines.Single().LotNo.Should().Be("B1");
        (await LotOnHandAsync(m.LotItem, m.Second)).Should().Equal(("B1", 5m));
        (await LotOnHandAsync(m.LotItem, m.Main)).Should().Equal(("A1", 4m), ("B1", 1m));
        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.LotItem, 1m, LotNo: "ZZ")), "MM-E-355");
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T07"), Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task LotShort_IsMmE356_EvenInAllowInMonth()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.LotItem, 4m, 10m, "A1");
        // Lot B1 keeps the item + warehouse month end above 0, so only the Lot balance stops the save
        await ReceiveAsync(m.LotItem, 2m, 10m, "B1", new DateOnly(2026, 11, 20));

        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.LotItem, 5m, LotNo: "A1")), "MM-E-356");
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T08"), Trait("Id", "MM-UT-001"), Trait("Rule", "MM-BR-001"), Trait("Rule", "MM-BR-602")]
    public async Task Deny_MoreThanTheSendingWarehouseHolds_IsMmE001()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 2m, 10m);

        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)), "MM-E-001");
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T09"), Trait("Id", "MM-UT-002"), Trait("Rule", "MM-BR-002")]
    public async Task AllowInMonth_ANegativeBalanceInTheMonth_IsSaved_AndMarked()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 2m, 10m);
        await ReceiveAsync(m.Item, 2m, 10m, date: new DateOnly(2026, 11, 20));

        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)), "kim", None);

        (await MovementsAsync(dto.Header.TransferNo)).Select(x => (x.WarehouseId, x.NegativeStock))
            .Should().Equal((m.Main, true), (m.Second, false));
    }

    [DatabaseFact, Trait("Id", "MM-UC-018-T10"), Trait("Id", "MM-UT-009"), Trait("Rule", "MM-BR-009")]
    public async Task Deny_APastTransferThatMakesALaterBalanceNegative_IsMmE009()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 3m, 10m);
        await IssueAsync(m.Item, m.Main, 2m, new DateOnly(2026, 11, 6));
        // Stock now is 6, so only the balance of the 6th (−1) stops the save
        await ReceiveAsync(m.Item, 5m, 10m, date: new DateOnly(2026, 11, 20));

        await ShouldFailAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 2m)) with { TransferDt = new DateOnly(2026, 11, 5) }, "MM-E-009");
    }

    // October has no deal on its last day: the month-end balance is the one after the 20th
    [DatabaseFact, Trait("Id", "MM-UC-018-T11"), Trait("Id", "MM-UT-010"), Trait("Rule", "MM-BR-010")]
    public async Task AllowInMonth_ANegativeMonthEndBalance_IsMmE010()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m, date: new DateOnly(2026, 11, 2));

        await ShouldFailAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m)) with { TransferDt = new DateOnly(2026, 10, 20) }, "MM-E-010");
    }

    [DatabaseFact, Trait("Id", "MM-UT-715"), Trait("Id", "MM-UT-713"), Trait("Rule", "MM-BR-715"), Trait("Rule", "MM-BR-713")]
    public async Task LocationTransfer_MovesBetweenLocationsOfOneWarehouse_AndTheSameLocationIsMmE715()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.LocItem, 5m, 10m);

        var dto = await Service.CreateAsync(
            Request(m, null, new SaveStockTransferLineRequest(m.LocItem, 2m, ToLocationId: m.Shelf)) with { MovType = "lt" }, "kim", None);

        dto.Header.Should().BeEquivalentTo(new { MovType = "LT", FromWarehouseId = m.Main, ToWarehouseId = m.Main });
        dto.Header.TransferNo.Should().StartWith("TR2611");
        dto.Lines.Single().Should().BeEquivalentTo(new { FromLocationCode = "*", ToLocationCode = "A1" });
        (await LocationOnHandAsync(m.LocItem, m.Main)).Should().BeEquivalentTo(new[] { ("*", 3m), ("A1", 2m) });
        await ShouldFailAsync(
            Request(m, null, new SaveStockTransferLineRequest(m.LocItem, 1m)) with { MovType = "LT" }, StockTransferRules.InvalidCode, "LT", 1);
        await ShouldFailAsync(
            Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m, ToLocationId: m.Shelf)), "MM-E-713");
    }

    [DatabaseFact, Trait("Id", "MM-UT-912"), Trait("Rule", "MM-BR-912"), Trait("Rule", "MM-BR-916")]
    public async Task PalletLine_MovesThePalletWhole_DeleteMovesItBack_AndAChangedPalletStopsTheDelete()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 10m, 10m);
        var pallet = await PalletAsync(m, 4m);

        await ShouldFailAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 3m, PalletId: pallet)), StockTransferRules.PalletCode,
            1, "PL" + m.Key);
        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 4m, PalletId: pallet)), "kim", None);

        dto.Lines.Single().PalletNo.Should().Be("PL" + m.Key);
        (await PalletPlaceAsync(pallet)).Should().Be((m.Second, "*", "ACTIVE"));
        (await PalletEventsAsync(pallet)).Should().Equal(("MOVE", 4m, dto.Header.TransferNo));

        await Service.DeleteAsync(dto.Header.Id, dto.Header.UpdatedAt, "kim", None);
        (await PalletPlaceAsync(pallet)).Should().Be((m.Main, "*", "ACTIVE"));
        (await PalletEventsAsync(pallet)).Should().HaveCount(2);

        var again = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 4m, PalletId: pallet)), "kim", None);
        await ExecuteAsync("UPDATE mm_pallet SET qty = 3 WHERE id = @Id", pallet);
        var undo = () => Service.DeleteAsync(again.Header.Id, again.Header.UpdatedAt, "kim", None);
        var error = (await undo.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((StockTransferRules.PalletCode, new object[] { 1, "PL" + m.Key }));
    }

    [DatabaseFact]
    public async Task Search_FindsByEitherWarehouse_TypeAndNumberStart()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.Item, 5m, 10m);
        var dto = await Service.CreateAsync(Request(m, m.Second, new SaveStockTransferLineRequest(m.Item, 1m)), "kim", None);

        (await Service.SearchAsync(new StockTransferSearchQuery { WarehouseId = m.Second }, None)).Select(s => s.Id).Should().Equal(dto.Header.Id);
        (await Service.SearchAsync(new StockTransferSearchQuery { TransferNo = dto.Header.TransferNo.ToLowerInvariant(), MovType = "tr" }, None))
            .Select(s => s.Id).Should().Equal(dto.Header.Id);
        (await Service.SearchAsync(new StockTransferSearchQuery { WarehouseId = m.Second, MovType = "LT" }, None)).Should().BeEmpty();
        var wrong = () => Service.SearchAsync(new StockTransferSearchQuery { MovType = "GI" }, None);
        (await wrong.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("Common-E-002");
    }

    private static SaveStockTransferRequest Request(Masters m, long? to, params SaveStockTransferLineRequest[] lines) =>
        new("TR", Day, m.Main, to, " Move ", lines);

    private async Task ShouldFailAsync(SaveStockTransferRequest request, string code, params object[] args)
    {
        var act = () => Service.CreateAsync(request, "kim", None);
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(code);
        if (args.Length > 0)
        {
            error.Args.Should().BeEquivalentTo(args, o => o.WithStrictOrdering());
        }
    }

    private sealed record Masters(
        string Key, long Item, long LotItem, long LocItem, long Service, long Main, long Second, long Consignment, string ConsignmentCode,
        long Shelf);

    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Item, long LotItem, long LocItem, long Service, long Main, long Second, long Consignment, string ConsignmentCode)>(
            """
            WITH i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 l AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('L' || @Key, 'Resin', 'INVENTORY', true, DATE '2026-10-01', 't', 't') RETURNING id),
                 c AS (INSERT INTO bc_item (item_cd, item_name, type, location_managed, created_by, updated_by)
                       VALUES ('C' || @Key, 'Cable', 'INVENTORY', true, 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id),
                 w AS (INSERT INTO bc_warehouse (warehouse_code, warehouse_name, business_place_id, created_by, updated_by)
                       VALUES ('W' || @Key, 'Second', (SELECT id FROM bc_business_place WHERE is_default), 't', 't') RETURNING id)
            SELECT (SELECT id FROM i), (SELECT id FROM l), (SELECT id FROM c), (SELECT id FROM s),
                   (SELECT id FROM bc_warehouse WHERE is_default), (SELECT id FROM w),
                   (SELECT id FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT'),
                   (SELECT warehouse_code FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT')
            """,
            new { Key = key });
        await connection.ExecuteAsync(
            StarKeys.Ensure("@Main, @Second", "@Item, @LotItem, @LocItem"), new { ids.Main, ids.Second, ids.Item, ids.LotItem, ids.LocItem });
        var shelf = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO bc_location (warehouse_id, location_code, created_by, updated_by)
            VALUES (@Main, 'A1', 't', 't') ON CONFLICT (warehouse_id, location_code) DO NOTHING;
            SELECT id FROM bc_location WHERE warehouse_id = @Main AND location_code = 'A1';
            """,
            new { ids.Main });
        return new Masters(key, ids.Item, ids.LotItem, ids.LocItem, ids.Service, ids.Main, ids.Second, ids.Consignment, ids.ConsignmentCode, shelf);
    }

    // Receives stock in the default warehouse at this cost on Day (or the date given)
    private async Task ReceiveAsync(long item, decimal qty, decimal price, string? lotNo = null, DateOnly? date = null)
    {
        var main = (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", new { })).Single();
        await PostAsync(new StockMovementRequest("GR", date ?? Day, item, main, qty, price, "GR-TEST", 1, LotNo: lotNo));
    }

    private Task IssueAsync(long item, long warehouse, decimal qty, DateOnly? date = null) =>
        PostAsync(new StockMovementRequest("GI", date ?? Day, item, warehouse, -qty, RefKey: "GI-TEST", RefSeq: 1));

    private async Task PostAsync(StockMovementRequest request)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.PostAsync([request], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    // An ACTIVE Pallet of the plain item's stock at '*' of the default warehouse
    private async Task<long> PalletAsync(Masters m, decimal qty)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            $"""
            INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, status, created_by, updated_by)
            VALUES ('PL' || @Key, @Item, {StarKeys.Lot("@Item")}, @Main, {StarKeys.Location("@Main")}, @Qty, 'ACTIVE', 't', 't')
            RETURNING id
            """,
            new { m.Key, m.Item, m.Main, Qty = qty });
    }

    private async Task ModeAsync(string negativeStockMode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync("UPDATE mm_inventory_setting SET negative_stock_mode = @Mode", new { Mode = negativeStockMode });
    }

    private async Task<decimal> OnHandAsync(long item, long warehouse) =>
        (await QueryAsync<decimal?>(
            "SELECT sum(onhand_qty) FROM mm_onhand WHERE item_id = @Item AND warehouse_id = @Warehouse", new { Item = item, Warehouse = warehouse }))
        .SingleOrDefault() ?? 0m;

    private async Task<List<(string LotNo, decimal Qty)>> LotOnHandAsync(long item, long warehouse) =>
        await QueryAsync<(string, decimal)>(
            """
            SELECT l.lot_no, sum(o.onhand_qty) FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
            WHERE o.item_id = @Item AND o.warehouse_id = @Warehouse AND o.onhand_qty <> 0
            GROUP BY l.lot_no ORDER BY l.lot_no
            """,
            new { Item = item, Warehouse = warehouse });

    private async Task<List<(string LocationCode, decimal Qty)>> LocationOnHandAsync(long item, long warehouse) =>
        await QueryAsync<(string, decimal)>(
            """
            SELECT c.location_code, sum(o.onhand_qty) FROM mm_onhand o JOIN bc_location c ON c.id = o.location_id
            WHERE o.item_id = @Item AND o.warehouse_id = @Warehouse AND o.onhand_qty <> 0
            GROUP BY c.location_code ORDER BY c.location_code
            """,
            new { Item = item, Warehouse = warehouse });

    private async Task<(decimal OnHand, decimal Value, decimal Cost)> ItemValueAsync(long item) =>
        (await QueryAsync<(decimal, decimal, decimal)>(
            "SELECT coalesce(onhand_qty, 0), coalesce(stk_val, 0), coalesce(mvg_cost, 0) FROM bc_item WHERE id = @Item", new { Item = item }))
        .Single();

    // Original movements of a document in id order: type, warehouse, qty, amount and the negative stock mark
    private async Task<List<(string MovType, long WarehouseId, decimal Qty, decimal Amount, bool NegativeStock)>> MovementsAsync(string no) =>
        await QueryAsync<(string, long, decimal, decimal, bool)>(
            """
            SELECT mov_type, warehouse_id, qty, amount, negative_stock FROM mm_goods_movement
            WHERE ref_key = @No AND parent_movement_id IS NULL ORDER BY id
            """,
            new { No = no });

    private async Task<List<(string MovType, long WarehouseId, decimal Qty, DateOnly DocumentDt)>> ReversalsAsync(string no) =>
        await QueryAsync<(string, long, decimal, DateOnly)>(
            "SELECT mov_type, warehouse_id, qty, document_dt FROM mm_goods_movement WHERE ref_key = @No AND parent_movement_id IS NOT NULL ORDER BY id",
            new { No = no });

    private async Task<(long WarehouseId, string LocationCode, string Status)> PalletPlaceAsync(long pallet) =>
        (await QueryAsync<(long, string, string)>(
            "SELECT p.warehouse_id, c.location_code, p.status FROM mm_pallet p JOIN bc_location c ON c.id = p.location_id WHERE p.id = @Id",
            new { Id = pallet })).Single();

    private async Task<List<(string EventType, decimal Qty, string RefKey)>> PalletEventsAsync(long pallet) =>
        await QueryAsync<(string, decimal, string)>(
            "SELECT event_type, qty, ref_key FROM mm_pallet_event WHERE pallet_id = @Id ORDER BY id", new { Id = pallet });

    private async Task<int> CountAsync(string sql, long id) => (await QueryAsync<int>(sql, new { Id = id })).Single();

    private async Task ExecuteAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    private async Task<List<T>> QueryAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => StockTransferServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Sd.Assemblies;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.Assemblies;

/// <summary>Own schema for the assembly tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class AssemblySchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "AssemblySchema";
}

/// <summary>
/// Set assembly and disassembly on real PostgreSQL (SD-UC-055, TASK-SD-072). Without a company the base currency is USD. Each test
/// makes its own Set of two plain components C1 (2 per Set, cost 50) and C2 (1 per Set, cost 30), or of a Lot-managed component,
/// receives them in the default warehouse on <see cref="Day"/> and sets the negative stock mode it needs. Today is
/// <see cref="Today"/>.
/// </summary>
[Collection(AssemblySchemaDefinition.Name)]
public class AssemblyServiceTests(AuthSchemaFixture db)
{
    private const string Deny = "DENY";
    private const string AllowInMonth = "ALLOW_IN_MONTH";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Day = new(2026, 11, 4);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private AssemblyService Service => new(
        db.Get<IAssemblyRepository>(), db.Get<IInventoryRepository>(), db.Get<ISetComponentRepository>(), db.Get<ILocalizationRepository>(),
        Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    [DatabaseFact, Trait("Id", "SD-UC-055-T01"), Trait("Id", "SD-UT-514"), Trait("Id", "SD-UT-515"), Trait("Rule", "SD-BR-514"),
     Trait("Rule", "SD-BR-515")]
    public async Task Assemble_IssuesTheComponents_ReceivesTheSetAtTheirAmount()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);

        var created = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null, "First"), "kim", None);

        created.AssemblyNo.Should().MatchRegex(@"^AS2611\d{6}$");
        created.SetLotNo.Should().Be("*");
        var dto = await Service.GetAsync(created.Id, None);
        dto.Header.Should().BeEquivalentTo(new
        {
            Kind = "ASSEMBLE",
            AssemblyDt = Day,
            SetItemId = m.Set,
            Qty = 3m,
            LocationCode = "*",
            SetLotNo = "*",
            Status = "OP",
            Amt = 390m,
            QtyLeft = 3m,
        });
        dto.Remarks.Should().Be("First");
        dto.Lines.Select(l => (l.ItemId, l.Qty, l.LotNo, l.Prc, l.Amt)).Should().Equal((m.C1, 6m, "*", 50m, 300m), (m.C2, 3m, "*", 30m, 90m));
        (await MovementsAsync(created.AssemblyNo)).Should().Equal(
            ("BI", m.C1, -6m, -300m), ("BI", m.C2, -3m, -90m), ("BP", m.Set, 3m, 390m));
        (await PriceAsync(created.AssemblyNo, "BP")).Should().Be(130m);
        (await OnHandAsync(m.C1), await OnHandAsync(m.C2), await OnHandAsync(m.Set)).Should().Be((14m, 7m, 3m));
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T02"), Trait("Id", "SD-UT-518"), Trait("Rule", "MM-BR-368"), Trait("Rule", "SD-BR-518")]
    public async Task LotManagedSet_GetsANewAutomaticLot_LinkedToTheComponentLotsItUsed()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync(lotSet: true, lotComponent: true);
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 3m, 30m, "A1");
        await ReceiveAsync(m.C2, 5m, 40m, "B1");

        var created = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 4m, Day, m.Main, null), "kim", None);

        created.SetLotNo.Should().MatchRegex(@"^261104-\d{3}$");
        var dto = await Service.GetAsync(created.Id, None);
        dto.Header.SetLotNo.Should().Be(created.SetLotNo);
        dto.Lines.Select(l => (l.ItemId, l.LotNo, l.Qty)).Should().Equal((m.C1, "*", 8m), (m.C2, "A1", 3m), (m.C2, "B1", 1m));
        (await LinksAsync(created.Id)).Should().BeEquivalentTo(new[] { ("A1", 3m), ("B1", 1m) });
        (await QueryAsync<long?>(
            "SELECT assembly_id FROM mm_lot WHERE item_id = @Item AND lot_no = @No", new { Item = m.Set, No = created.SetLotNo }))
            .Should().Equal(created.Id);
    }

    [DatabaseTheory, Trait("Id", "SD-UC-055-T03"), Trait("Id", "SD-UT-514"), Trait("Rule", "SD-BR-514")]
    [InlineData(Deny)]
    [InlineData(AllowInMonth)]
    public async Task ShortComponent_IsRefusedWithSdE514_InEveryNegativeStockMode(string mode)
    {
        await ModeAsync(mode);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 2m, 30m);

        var act = () => Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("SD-E-514", new object[] { "S" + m.Key, "C2" + m.Key, m.MainCode, 3m, 2m }));
        (await CountAsync("SELECT count(*) FROM mm_assembly WHERE set_item_id = @Id", m.Set)).Should().Be(0);
        (await OnHandAsync(m.C1)).Should().Be(20m);
    }

    [DatabaseFact, Trait("Id", "SD-UT-509"), Trait("Rule", "SD-BR-509")]
    public async Task SetWithoutComponents_IsRefusedWithSdE509_AndAnotherItemWithCommonE002()
    {
        var m = await MastersAsync();
        await ExecuteAsync("DELETE FROM sd_bom WHERE parent_item_id = @Id", m.Set);

        var empty = () => Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 1m, Day, m.Main, null), "kim", None);
        var notSet = () => Service.AssembleAsync(new SaveAssemblyRequest(m.C1, 1m, Day, m.Main, null), "kim", None);

        (await empty.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-509");
        (await notSet.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("setItemId", m.C1);
    }

    // C2 is issued at its moving average cost (3 × 30 + 5 × 40) / 8 = 36.25 and comes back at that price
    [DatabaseFact, Trait("Id", "SD-UC-055-T04"), Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516")]
    public async Task Disassemble_GivesTheComponentsBack_ToTheLastUsedLotsFirst_AtTheAssemblyPrices()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync(lotSet: true, lotComponent: true);
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 3m, 30m, "A1");
        await ReceiveAsync(m.C2, 5m, 40m, "B1");
        var assembly = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 4m, Day, m.Main, null), "kim", None);

        var first = await Service.DisassembleAsync(assembly.Id, new DisassembleRequest(2m, Day.AddDays(1), null), "kim", None);
        var second = await Service.DisassembleAsync(assembly.Id, new DisassembleRequest(1m, Day.AddDays(2), null), "kim", None);
        var tooMuch = () => Service.DisassembleAsync(assembly.Id, new DisassembleRequest(2m, Day.AddDays(2), null), "kim", None);

        first.SetLotNo.Should().Be(assembly.SetLotNo);
        var d1 = await Service.GetAsync(first.Id, None);
        d1.Header.Should().BeEquivalentTo(new { Kind = "DISASSEMBLE", Qty = 2m, SourceAssemblyNo = assembly.AssemblyNo, SetLotNo = assembly.SetLotNo });
        d1.Lines.Select(l => (l.ItemId, l.LotNo, l.Qty, l.Prc)).Should().BeEquivalentTo(new[] { (m.C1, "*", 4m, 50m), (m.C2, "B1", 1m, 36.25m), (m.C2, "A1", 1m, 36.25m) });
        (await Service.GetAsync(second.Id, None)).Lines.Select(l => (l.ItemId, l.LotNo, l.Qty)).Should().BeEquivalentTo(new[] { (m.C1, "*", 2m), (m.C2, "A1", 1m) });
        (await MovementsAsync(first.AssemblyNo)).Where(x => x.MovType == "DA").Should().Equal(("DA", m.Set, -2m, -2m * (await PriceAsync(assembly.AssemblyNo, "BP"))));
        (await Service.GetAsync(assembly.Id, None)).Header.QtyLeft.Should().Be(1m);
        var error = (await tooMuch.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("SD-E-516", new object[] { assembly.AssemblyNo, 1m }));
        (await OnHandAsync(m.Set), await OnHandAsync(m.C1), await OnHandAsync(m.C2)).Should().Be((1m, 18m, 7m));
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T05"), Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516"), Trait("Rule", "MM-BR-601")]
    public async Task Void_AnAssemblyWithADisassembly_IsRefused_UntilTheDisassemblyIsVoided()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        var assembly = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);
        var part = await Service.DisassembleAsync(assembly.Id, new DisassembleRequest(1m, Day, null), "kim", None);

        var blocked = async () => await Service.VoidAsync(assembly.Id, new VoidAssemblyRequest(await UpdatedAtAsync(assembly.Id)), "kim", None);
        var error = (await blocked.Should().ThrowAsync<ConflictException>()).Which;
        var voidedPart = await Service.VoidAsync(part.Id, new VoidAssemblyRequest(await UpdatedAtAsync(part.Id)), "kim", None);
        var voided = await Service.VoidAsync(assembly.Id, new VoidAssemblyRequest(await UpdatedAtAsync(assembly.Id)), "kim", None);
        var again = async () => await Service.VoidAsync(assembly.Id, new VoidAssemblyRequest(voided.UpdatedAt), "kim", None);

        (error.Code, error.Args).Should().BeEquivalentTo(("SD-E-516", new object[] { assembly.AssemblyNo, 2m }));
        (voidedPart.Status, voided.Status, voided.QtyLeft).Should().Be(("VO", "VO", 0m));
        await again.Should().ThrowAsync<ConcurrencyException>();
        (await OnHandAsync(m.C1), await OnHandAsync(m.C2), await OnHandAsync(m.Set)).Should().Be((20m, 10m, 0m));
        (await QueryAsync<DateTime>("SELECT DISTINCT document_dt FROM mm_goods_movement WHERE parent_movement_id IS NOT NULL AND ref_key = @No",
            new { No = assembly.AssemblyNo })).Select(DateOnly.FromDateTime).Should().Equal(Today);
        (await Service.SearchAsync(new() { SetItemId = m.Set }, None)).Select(r => (r.AssemblyNo, r.Status))
            .Should().BeEquivalentTo(new[] { (assembly.AssemblyNo, "VO"), (part.AssemblyNo, "VO") });
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T07"), Trait("Rule", "MM-BR-356")]
    public async Task Disassemble_ASetLotAlreadyIssued_IsRefusedByTheLotBalance_EvenInAllowInMonth()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync(lotSet: true);
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        var assembly = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 2m, Day, m.Main, null), "kim", None);
        // Another Set Lot keeps the item's total above zero, so only the Lot balance can refuse
        await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 2m, Day, m.Main, null), "kim", None);
        var setLot = (await QueryAsync<long>(
            "SELECT id FROM mm_lot WHERE item_id = @Item AND lot_no = @No", new { Item = m.Set, No = assembly.SetLotNo })).Single();
        await PostAsync(new StockMovementRequest("GI", Day, m.Set, m.Main, -2m, RefKey: "GI-TEST", RefSeq: 1, LotId: setLot));

        var act = () => Service.DisassembleAsync(assembly.Id, new DisassembleRequest(1m, Day.AddDays(1), null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().BeOneOf(LotRules.NegativeCode, StockKeyRules.NegativeCode);
        (await CountAsync("SELECT count(*) FROM mm_assembly WHERE source_assembly_id = @Id", assembly.Id)).Should().Be(0);
    }

    // The component is there on the assembly date, but a later issue already took it, so the on-hand now would go below zero
    [DatabaseFact, Trait("Id", "SD-UC-055-T08"), Trait("Rule", "MM-BR-001")]
    public async Task Deny_RefusesAnAssemblyThatMakesTheOnHandNegative()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 10m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        await PostAsync(new StockMovementRequest("GI", new DateOnly(2026, 11, 20), m.C1, m.Main, -8m, RefKey: "GI-TEST", RefSeq: 1));

        var act = () => Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockPolicy.InsufficientStock);
        (await CountAsync("SELECT count(*) FROM mm_assembly WHERE set_item_id = @Id", m.Set)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T10"), Trait("Rule", "MM-BR-009")]
    public async Task Deny_RefusesABackDatedAssemblyThatMakesALaterDateNegative()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 10m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        await PostAsync(new StockMovementRequest("GI", new DateOnly(2026, 11, 20), m.C1, m.Main, -8m, RefKey: "GI-TEST", RefSeq: 1));
        await ReceiveAsync(m.C1, 10m, 50m, date: new DateOnly(2026, 11, 25));

        var act = () => Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeOnDate);
        (await CountAsync("SELECT count(*) FROM mm_assembly WHERE set_item_id = @Id", m.Set)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T09"), Trait("Rule", "MM-BR-002")]
    public async Task AllowInMonth_SavesAnAssemblyThatMakesAMidMonthDateNegative()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 10m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        await PostAsync(new StockMovementRequest("GI", new DateOnly(2026, 11, 20), m.C1, m.Main, -8m, RefKey: "GI-TEST", RefSeq: 1));
        await ReceiveAsync(m.C1, 10m, 50m, date: new DateOnly(2026, 11, 25));

        var created = await Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);

        (await Service.GetAsync(created.Id, None)).Header.Status.Should().Be("OP");
        (await OnHandAsync(m.C1), await OnHandAsync(m.Set)).Should().Be((6m, 3m));
    }

    [DatabaseFact, Trait("Id", "SD-UC-055-T11"), Trait("Rule", "MM-BR-010")]
    public async Task AllowInMonth_RefusesANegativeMonthEnd_WithNoMovementOnTheLastDay()
    {
        await ModeAsync(AllowInMonth);
        var m = await MastersAsync();
        await ReceiveAsync(m.C1, 10m, 50m);
        await ReceiveAsync(m.C2, 10m, 30m);
        await PostAsync(new StockMovementRequest("GI", new DateOnly(2026, 11, 20), m.C1, m.Main, -8m, RefKey: "GI-TEST", RefSeq: 1));

        var act = () => Service.AssembleAsync(new SaveAssemblyRequest(m.Set, 3m, Day, m.Main, null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeAtMonthEnd);
    }

    [DatabaseFact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518")]
    public async Task Preview_ShowsTheFifoPartsAndTheShortQty_WithoutSaving()
    {
        await ModeAsync(Deny);
        var m = await MastersAsync(lotComponent: true);
        await ReceiveAsync(m.C1, 20m, 50m);
        await ReceiveAsync(m.C2, 1m, 30m, "A1");

        var preview = await Service.PreviewAsync(new() { SetItemId = m.Set, Qty = 3m, WarehouseId = m.Main, AssemblyDt = Day }, None);

        preview.Components.Select(c => (c.ItemId, c.NeededQty, c.ShortQty)).Should().Equal((m.C1, 6m, 0m), (m.C2, 3m, 2m));
        preview.Components[1].Picks.Select(p => (p.LotNo, p.Qty, p.Prc)).Should().Equal(("A1", 1m, 30m));
        preview.Amt.Should().Be(330m);
        (await CountAsync("SELECT count(*) FROM mm_assembly WHERE set_item_id = @Id", m.Set)).Should().Be(0);
    }

    private sealed record Masters(string Key, long C1, long C2, long Set, long Main, string MainCode);

    // C1 (2 per Set) and C2 (1 per Set) and the Set item, in the default warehouse
    private async Task<Masters> MastersAsync(bool lotSet = false, bool lotComponent = false)
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long C1, long C2, long Set, long Main, string MainCode)>(
            """
            WITH c1 AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                        VALUES ('C1' || @Key, 'Frame', 'INVENTORY', 't', 't') RETURNING id),
                 c2 AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                        VALUES ('C2' || @Key, 'Panel', 'INVENTORY', @LotComponent, CASE WHEN @LotComponent THEN DATE '2026-10-01' END, 't', 't')
                        RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('S' || @Key, 'Gift Set', 'SET', @LotSet, CASE WHEN @LotSet THEN DATE '2026-10-01' END, 't', 't') RETURNING id),
                 b AS (INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by)
                       SELECT s.id, 1, c1.id, 2, 't', 't' FROM s, c1
                       UNION ALL
                       SELECT s.id, 2, c2.id, 1, 't', 't' FROM s, c2)
            SELECT (SELECT id FROM c1), (SELECT id FROM c2), (SELECT id FROM s), w.id, w.warehouse_code
            FROM bc_warehouse w WHERE w.is_default
            """,
            new { Key = key, LotSet = lotSet, LotComponent = lotComponent });
        await connection.ExecuteAsync(
            StarKeys.Ensure("@Main", "@C1, @C2, @Set"), new { ids.Main, ids.C1, ids.C2, ids.Set });
        return new Masters(key, ids.C1, ids.C2, ids.Set, ids.Main, ids.MainCode);
    }

    // Receives stock in the default warehouse at this cost on Day (or the date given)
    private Task ReceiveAsync(long item, decimal qty, decimal price, string? lotNo = null, DateOnly? date = null) =>
        PostAsync(new StockMovementRequest("GR", date ?? Day, item, 0, qty, price, "GR-TEST", 1, LotNo: lotNo));

    private async Task PostAsync(StockMovementRequest request)
    {
        var main = (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", new { })).Single();
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.PostAsync([request with { WarehouseId = main }], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task ModeAsync(string negativeStockMode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync("UPDATE mm_inventory_setting SET negative_stock_mode = @Mode", new { Mode = negativeStockMode });
    }

    private async Task<decimal> OnHandAsync(long item) =>
        (await QueryAsync<decimal?>("SELECT sum(onhand_qty) FROM mm_onhand WHERE item_id = @Item", new { Item = item })).SingleOrDefault() ?? 0m;

    // Original movements of a document in id order: type, item, qty and amount
    private async Task<List<(string MovType, long ItemId, decimal Qty, decimal Amount)>> MovementsAsync(string no) =>
        await QueryAsync<(string, long, decimal, decimal)>(
            "SELECT mov_type, item_id, qty, amount FROM mm_goods_movement WHERE ref_key = @No AND parent_movement_id IS NULL ORDER BY id",
            new { No = no });

    private async Task<decimal> PriceAsync(string no, string movType) =>
        (await QueryAsync<decimal>(
            "SELECT prc FROM mm_goods_movement WHERE ref_key = @No AND mov_type = @Type AND parent_movement_id IS NULL",
            new { No = no, Type = movType })).Single();

    private async Task<List<(string LotNo, decimal Qty)>> LinksAsync(long assemblyId) =>
        await QueryAsync<(string, decimal)>(
            "SELECT l.lot_no, k.qty FROM mm_lot_link k JOIN mm_lot l ON l.id = k.component_lot_id WHERE k.assembly_id = @Id",
            new { Id = assemblyId });

    private async Task<DateTimeOffset> UpdatedAtAsync(long id) => (await Service.GetAsync(id, None)).Header.UpdatedAt;

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

        public DateOnly Today() => AssemblyServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

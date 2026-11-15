using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Contracts.Mm.Pallets;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Pallets;

/// <summary>Own schema for the Pallet tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PalletSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "PalletSchema";
}

/// <summary>
/// Pallet split and merge on real PostgreSQL (MM-UC-035, TASK-MM-053). Each test makes its own item and Lots and puts its Pallets on
/// the default warehouse's Location '*'.
/// </summary>
[Collection(PalletSchemaDefinition.Name)]
public class PalletServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 11, 15);

    private PalletService Service => new(
        db.Get<IPalletRepository>(), new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-035-T01"), Trait("Id", "MM-UC-035-T04"), Trait("Id", "MM-UT-914"), Trait("Id", "MM-UT-916"),
     Trait("Rule", "MM-BR-914"), Trait("Rule", "MM-BR-916")]
    public async Task Split_MakesANewNumberOnTheSameKey_KeepsTheTotal_AndMovesNoStock()
    {
        var m = await MastersAsync();
        var source = await PalletAsync(m, m.Lot1, 10m);
        var read = await Service.GetAsync(source, None);

        var created = await Service.SplitAsync(source, new SplitPalletRequest(3m, read.UpdatedAt), "kim", None);

        created.PalletNo.Should().MatchRegex(@"^PL2611\d{6}$");
        var (kept, split) = (await Service.GetAsync(source, None), await Service.GetAsync(created.PalletId, None));
        (kept.Qty + split.Qty, kept.Qty, kept.Status).Should().Be((10m, 7m, "ACTIVE"));
        split.Should().BeEquivalentTo(new
        {
            kept.ItemId,
            kept.LotId,
            kept.WarehouseId,
            kept.LocationId,
            Qty = 3m,
            Status = "ACTIVE",
            ParentPalletNo = kept.PalletNo,
        });
        kept.Events!.Select(e => (e.EventType, e.EventDt, e.Qty, e.RelatedPalletNo)).Should().Equal(
            ("CREATE", Today, 10m, null), ("SPLIT", Today, -3m, created.PalletNo));
        split.Events!.Select(e => (e.EventType, e.Qty, e.RelatedPalletNo)).Should().Equal(("SPLIT", 3m, kept.PalletNo));
        (await MovementCountAsync(m.Item)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UT-914"), Trait("Rule", "MM-BR-914")]
    public async Task Split_TheWholeQty_OrAStaleRead_IsRefused_AndChangesNothing()
    {
        var m = await MastersAsync();
        var source = await PalletAsync(m, m.Lot1, 4m);
        var read = await Service.GetAsync(source, None);

        var whole = () => Service.SplitAsync(source, new SplitPalletRequest(4m, read.UpdatedAt), "kim", None);
        var stale = () => Service.SplitAsync(source, new SplitPalletRequest(1m, read.UpdatedAt.AddSeconds(-1)), "kim", None);

        (await whole.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PalletRules.SplitCode);
        await stale.Should().ThrowAsync<ConcurrencyException>();
        (await Service.GetAsync(source, None)).Should().BeEquivalentTo(new { Qty = 4m, read.UpdatedAt });
    }

    [DatabaseFact, Trait("Id", "MM-UC-035-T02"), Trait("Id", "MM-UC-035-T04"), Trait("Id", "MM-UT-915"), Trait("Rule", "MM-BR-915")]
    public async Task Merge_ClosesTheSourcesAtZero_AndTheTargetTakesTheTotal_WithoutStockMovement()
    {
        var m = await MastersAsync();
        var target = await PalletAsync(m, m.Lot1, 5m);
        var first = await PalletAsync(m, m.Lot1, 2m);
        var second = await PalletAsync(m, m.Lot1, 1.5m);

        var merged = await Service.MergeAsync(new MergePalletsRequest(target, [first, second]), "kim", None);

        (merged.Qty, merged.Status).Should().Be((8.5m, "ACTIVE"));
        var (a, b) = (await Service.GetAsync(first, None), await Service.GetAsync(second, None));
        merged.Events!.Select(e => (e.EventType, e.Qty, e.RelatedPalletNo)).Should().Equal(
            ("CREATE", 5m, null), ("MERGE", 2m, a.PalletNo), ("MERGE", 1.5m, b.PalletNo));
        (a.Qty, a.Status, b.Qty, b.Status).Should().Be((0m, "CLOSED", 0m, "CLOSED"));
        a.Events!.Select(e => (e.EventType, e.Qty, e.RelatedPalletNo)).Should().Equal(("CREATE", 2m, null), ("MERGE", -2m, merged.PalletNo));
        (await MovementCountAsync(m.Item)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UC-035-T03"), Trait("Id", "MM-UT-915"), Trait("Rule", "MM-BR-915")]
    public async Task Merge_AnotherLot_IsRefusedWithItsNumber_AndChangesNothing()
    {
        var m = await MastersAsync();
        var target = await PalletAsync(m, m.Lot1, 5m);
        var same = await PalletAsync(m, m.Lot1, 2m);
        var other = await PalletAsync(m, m.Lot2, 2m);

        var merge = () => Service.MergeAsync(new MergePalletsRequest(target, [same, other]), "kim", None);

        var error = (await merge.Should().ThrowAsync<DomainException>()).Which;
        var otherNo = (await Service.GetAsync(other, None)).PalletNo;
        (error.Code, error.Args).Should().BeEquivalentTo((PalletRules.MergeCode, new object[] { otherNo }));
        (await Service.GetAsync(target, None)).Qty.Should().Be(5m);
        (await Service.GetAsync(same, None)).Status.Should().Be("ACTIVE");
    }

    [DatabaseFact]
    public async Task Search_AndTheScanLookup_FindThePallets()
    {
        var m = await MastersAsync();
        var pallet = await PalletAsync(m, m.Lot2, 2m);
        var closed = await PalletAsync(m, m.Lot1, 1m);
        await Service.MergeAsync(new MergePalletsRequest(await PalletAsync(m, m.Lot1, 1m), [closed]), "kim", None);
        var number = (await Service.GetAsync(pallet, None)).PalletNo;

        var active = await Service.SearchAsync(new() { ItemId = m.Item, Status = "active" }, None);
        var byLot = await Service.SearchAsync(new() { ItemId = m.Item, LotNo = m.Lot2No.ToLowerInvariant() }, None);
        var scanned = await Service.GetByNoAsync($" {number.ToLowerInvariant()} ", None);
        var missing = () => Service.GetByNoAsync("PL0000000000", None);

        active.Should().HaveCount(2).And.OnlyContain(p => p.Status == "ACTIVE" && p.LocationCode == "*");
        byLot.Should().ContainSingle().Which.Id.Should().Be(pallet);
        scanned.Should().BeEquivalentTo(new { Id = pallet, Qty = 2m, LotNo = m.Lot2No, ItemCd = m.ItemCd, Events = (object?)null });
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    private sealed record Masters(long Item, string ItemCd, long Lot1, long Lot2, string Lot2No, long Warehouse, long Star);

    // An item with Lots L1 and L2 and the default warehouse with its Location '*'
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Item, long Lot1, long Lot2, long Warehouse)>(
            """
            WITH i AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('P' || @Key, 'Pallet Item', 'INVENTORY', true, '2026-10-01', 't', 't') RETURNING id),
                 l1 AS (INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) SELECT id, 'L1' || @Key, 't', 't' FROM i RETURNING id),
                 l2 AS (INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) SELECT id, 'L2' || @Key, 't', 't' FROM i RETURNING id)
            SELECT (SELECT id FROM i), (SELECT id FROM l1), (SELECT id FROM l2), (SELECT id FROM bc_warehouse WHERE is_default)
            """,
            new { Key = key });
        var star = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
            VALUES (@Warehouse, '*', true, 't', 't') ON CONFLICT (warehouse_id, location_code) DO NOTHING;
            SELECT id FROM bc_location WHERE warehouse_id = @Warehouse AND location_code = '*';
            """,
            new { ids.Warehouse });
        return new Masters(ids.Item, "P" + key, ids.Lot1, ids.Lot2, "L2" + key, ids.Warehouse, star);
    }

    // An ACTIVE Pallet with its CREATE event, numbered outside the PL sequence the splits use
    private async Task<long> PalletAsync(Masters m, long lot, decimal qty)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.QuerySingleAsync<long>(
            """
            WITH p AS (
              INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, created_by, updated_by)
              VALUES ('T' || @No, @Item, @Lot, @Warehouse, @Star, @Qty, 't', 't') RETURNING id
            ), e AS (
              INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, created_by, updated_by)
              SELECT id, 'CREATE', @Today, @Qty, 't', 't' FROM p
            )
            SELECT id FROM p
            """,
            new { No = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), m.Item, Lot = lot, m.Warehouse, m.Star, Qty = qty, Today });
    }

    private async Task<int> MovementCountAsync(long item)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM mm_goods_movement WHERE item_id = @Item", new { Item = item });
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => PalletServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

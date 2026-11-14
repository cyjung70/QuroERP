using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Locations;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Locations;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Locations;

/// <summary>Own schema for the Location master tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LocationSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LocationSchema";
}

/// <summary>Location master batch save on real PostgreSQL (MM-UC-034, TASK-MM-051). Each test adds its own warehouse.</summary>
[Collection(LocationSchemaDefinition.Name)]
public class LocationServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 10, 1);

    private LocationService Service => new(db.Get<ILocationRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-034-T01"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712"), Trait("Rule", "MM-BR-713")]
    public async Task ANewWarehouse_HasOnlyTheLockedSystemLocationStar()
    {
        var warehouse = await WarehouseAsync("L01");

        var rows = await Service.SearchAsync(new() { WarehouseId = warehouse, IncludeInactive = true }, None);

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { WarehouseId = warehouse, LocationCode = "*", IsSystem = true, UseYn = true, OnhandQty = 0m });
    }

    [DatabaseFact, Trait("Id", "MM-UC-034-T02"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public async Task Save_AddsAndChangesRowsInOneBatch_WithCodesInUpperCase_StarFirst()
    {
        var warehouse = await WarehouseAsync("L02");
        var added = await Service.SaveAsync(Batch(added: [New(warehouse, " b-02 "), New(warehouse, "A-01")]), "kim", None);
        var b = added.Single(l => l.LocationCode == "B-02");

        var saved = await Service.SaveAsync(
            Batch(added: [New(warehouse, "c_03")], updated: [Row(b) with { LocationCode = null, LocationName = " Shelf B ", Remarks = " " }]), "lee", None);

        saved.Select(l => l.LocationCode).Should().Equal("*", "A-01", "B-02", "C_03");
        saved.Single(l => l.LocationCode == "B-02").Should().BeEquivalentTo(new { LocationName = "Shelf B", Remarks = (string?)null, UseYn = true });
        saved.Single(l => l.LocationCode == "B-02").UpdatedAt.Should().BeAfter(b.UpdatedAt);
        (await Service.SearchAsync(new() { WarehouseId = warehouse, Code = "b" }, None)).Select(l => l.LocationCode).Should().Equal("B-02");
    }

    [DatabaseFact, Trait("Id", "MM-UC-034-T03"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public async Task Save_RejectsADuplicateCodeInTheWarehouse_AndAChangedCode_AndSavesNothing()
    {
        var warehouse = await WarehouseAsync("L03");
        var other = await WarehouseAsync("L03B");
        var stored = (await Service.SaveAsync(Batch(added: [New(warehouse, "A-01"), New(other, "A-01")]), "kim", None))
            .Single(l => l.WarehouseId == warehouse && l.LocationCode == "A-01");

        var duplicate = () => Service.SaveAsync(Batch(added: [New(warehouse, "X-01"), New(warehouse, "a-01")]), "kim", None);
        var changed = () => Service.SaveAsync(Batch(updated: [Row(stored) with { LocationCode = "A-02" }]), "kim", None);

        (await duplicate.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A-01");
        (await changed.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LocationRules.InvalidCode);
        (await Codes(warehouse)).Should().Equal("*", "A-01");
    }

    [DatabaseFact, Trait("Id", "MM-UC-034-T04"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public async Task Save_TheSystemLocation_CannotBeChangedSetNotInUseOrDeleted()
    {
        var star = (await Service.SearchAsync(new() { WarehouseId = await WarehouseAsync("L04") }, None)).Single();

        var rename = () => Service.SaveAsync(Batch(updated: [Row(star) with { LocationName = "Star" }]), "kim", None);
        var inactive = () => Service.SaveAsync(Batch(updated: [Row(star) with { UseYn = false }]), "kim", None);
        var delete = () => Service.SaveAsync(Batch(deleted: [new LocationKey(star.Id, star.UpdatedAt)]), "kim", None);

        foreach (var act in new[] { rename, inactive, delete })
        {
            (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(new { Code = LocationRules.InvalidCode, Args = new object[] { "*" } });
        }
    }

    [DatabaseFact, Trait("Id", "MM-UC-034-T05"), Trait("Id", "MM-UT-717"), Trait("Rule", "MM-BR-717")]
    public async Task Save_ALocationWithABalance_CannotBeSetNotInUseOrDeleted()
    {
        var warehouse = await WarehouseAsync("L05");
        var location = (await Service.SaveAsync(Batch(added: [New(warehouse, "A-01")]), "kim", None)).Single(l => l.LocationCode == "A-01");
        await OnhandAsync(await ItemAsync("ZZL05"), warehouse, location.Id, 4);
        location = (await Service.SearchAsync(new() { WarehouseId = warehouse }, None)).Single(l => l.Id == location.Id);

        var inactive = () => Service.SaveAsync(Batch(updated: [Row(location) with { UseYn = false }]), "kim", None);
        var delete = () => Service.SaveAsync(Batch(deleted: [new LocationKey(location.Id, location.UpdatedAt)]), "kim", None);

        location.OnhandQty.Should().Be(4);
        (await inactive.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(
            new { Code = LocationRules.InUseCode, Args = new object[] { "A-01", LocationBlockReasons.OnHand } });
        (await delete.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A-01", LocationBlockReasons.OnHand);
    }

    [DatabaseFact, Trait("Id", "MM-UT-717"), Trait("Rule", "MM-BR-717")]
    public async Task Save_ALocationWithMovements_CanBeSetNotInUse_ButNotDeleted_AndAnUnusedOneCanBeDeleted()
    {
        var warehouse = await WarehouseAsync("L07");
        var rows = await Service.SaveAsync(Batch(added: [New(warehouse, "A-01"), New(warehouse, "B-01")]), "kim", None);
        var (moved, unused) = (rows.Single(l => l.LocationCode == "A-01"), rows.Single(l => l.LocationCode == "B-01"));
        await PostAsync(new("GR", Today, await ItemAsync("ZZL07"), warehouse, 1, Price: 1));
        await ExecuteAsync("UPDATE mm_goods_movement SET location_id = @Location WHERE warehouse_id = @Warehouse", new { Location = moved.Id, Warehouse = warehouse });

        var delete = () => Service.SaveAsync(Batch(deleted: [new LocationKey(moved.Id, moved.UpdatedAt)]), "kim", None);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A-01", LocationBlockReasons.HasMovement);

        var saved = await Service.SaveAsync(
            Batch(updated: [Row(moved) with { UseYn = false }], deleted: [new LocationKey(unused.Id, unused.UpdatedAt)]), "kim", None);
        saved.Select(l => (l.LocationCode, l.UseYn)).Should().Equal(("*", true), ("A-01", false));
    }

    [DatabaseFact, Trait("Id", "MM-UC-034-T06"), Trait("Id", "MM-UT-717"), Trait("Rule", "MM-BR-717"), Trait("Rule", "MM-BR-713")]
    public async Task Options_ListLocationsInUse_StarFirst_AndSearchShowsOthersOnlyWithIncludeInactive()
    {
        var warehouse = await WarehouseAsync("L06");
        var rows = await Service.SaveAsync(Batch(added: [New(warehouse, "B-01"), New(warehouse, "A-01")]), "kim", None);
        await Service.SaveAsync(Batch(updated: [Row(rows.Single(l => l.LocationCode == "B-01")) with { UseYn = false }]), "kim", None);

        (await Service.GetOptionsAsync(warehouse, None)).Select(o => o.LocationCode).Should().Equal("*", "A-01");
        (await Service.SearchAsync(new() { WarehouseId = warehouse }, None)).Select(l => l.LocationCode).Should().Equal("*", "A-01");
        (await Codes(warehouse)).Should().Equal("*", "A-01", "B-01");
    }

    [DatabaseFact]
    public async Task Save_ANewRowWithoutAnExistingWarehouse_IsRefused()
    {
        var none = () => Service.SaveAsync(Batch(added: [New(0, "A-01") with { WarehouseId = null }]), "kim", None);
        var unknown = () => Service.SaveAsync(Batch(added: [New(-1, "A-01")]), "kim", None);

        (await none.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LocationService.RequiredCode);
        (await unknown.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LocationService.RequiredCode);
    }

    [DatabaseFact]
    public async Task Save_ARowChangedAfterItWasRead_IsAConcurrencyError()
    {
        var warehouse = await WarehouseAsync("L08");
        var location = (await Service.SaveAsync(Batch(added: [New(warehouse, "A-01")]), "kim", None)).Single(l => l.LocationCode == "A-01");
        await Service.SaveAsync(Batch(updated: [Row(location) with { LocationName = "First" }]), "kim", None);

        var act = () => Service.SaveAsync(Batch(updated: [Row(location) with { LocationName = "Second" }]), "lee", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private static LocationSaveRow New(long warehouseId, string code) => new(null, warehouseId, code, null, null, null, null);

    private static LocationSaveRow Row(LocationDto l) =>
        new(l.Id, l.WarehouseId, l.LocationCode, l.LocationName, l.UseYn, l.Remarks, l.UpdatedAt);

    private static BatchRequest<LocationSaveRow, LocationKey> Batch(
        IReadOnlyList<LocationSaveRow>? added = null, IReadOnlyList<LocationSaveRow>? updated = null, IReadOnlyList<LocationKey>? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private async Task<IReadOnlyList<string>> Codes(long warehouseId) =>
        [.. (await Service.SearchAsync(new() { WarehouseId = warehouseId, IncludeInactive = true }, None)).Select(l => l.LocationCode)];

    private async Task<long> WarehouseAsync(string code)
    {
        var warehouses = new WarehouseService(db.Get<IWarehouseRepository>(), new FixedClock(), db.Get<IUnitOfWorkFactory>());
        var saved = await warehouses.SaveAsync(
            new BatchRequest<WarehouseSaveRow, WarehouseKey>([new(null, code, "Name " + code, false, true, null, null, null)], [], []), "kim", None);
        return saved.Single(w => w.WarehouseCode == code).Id;
    }

    private async Task<long> ItemAsync(string code) =>
        (await QueryAsync<long>(
            "INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by) VALUES (@Code, 'Item', 'INVENTORY', 't', 't') RETURNING id",
            new { Code = code })).Single();

    private async Task OnhandAsync(long item, long warehouse, long location, decimal qty)
    {
        await ExecuteAsync(StarKeys.Ensure("@Warehouse", "@Item"), new { Item = item, Warehouse = warehouse });
        await ExecuteAsync(
            $"""
            INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
            VALUES (@Item, @Warehouse, @Location, {StarKeys.Lot("@Item")}, @Qty, 't', 't')
            """,
            new { Item = item, Warehouse = warehouse, Location = location, Qty = qty });
    }

    private async Task PostAsync(StockMovementRequest request)
    {
        var inventory = new InventoryService(
            db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
            new DocumentNumberService(db.Get<IDocumentNumberRepository>()));
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await inventory.PostAsync([request], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }

    private async Task ExecuteAsync(string sql, object args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, args);
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => LocationServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

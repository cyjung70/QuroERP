using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Warehouses;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;
using QuroERP.Database;

namespace QuroErp.Infrastructure.Tests.Mm.Warehouses;

/// <summary>Own schema for the warehouse master tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class WarehouseSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "WarehouseSchema";
}

/// <summary>
/// Warehouse master batch save on real PostgreSQL (MM-UC-017, TASK-MM-025). Each test adds its own warehouses with its own codes,
/// and a test that moves the default warehouse moves it back.
/// </summary>
[Collection(WarehouseSchemaDefinition.Name)]
public class WarehouseServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 10, 1);

    private WarehouseService Service => new(db.Get<IWarehouseRepository>(), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-017-T01"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public async Task Save_AddsAndChangesRowsInOneBatch_WithCodesInUpperCase()
    {
        var added = await Service.SaveAsync(Batch(added: [New(" t01a "), New("T01B")]), "kim", None);
        var b = added.Single(w => w.WarehouseCode == "T01B");

        var saved = await Service.SaveAsync(
            Batch(added: [New("T01C")], updated: [Row(b) with { WarehouseName = " Renamed ", Remarks = " note " }]), "lee", None);

        saved.Select(w => w.WarehouseCode).Should().ContainInOrder("T01A", "T01B", "T01C");
        saved.Single(w => w.WarehouseCode == "T01B").Should().BeEquivalentTo(
            new { WarehouseName = "Renamed", Remarks = "note", IsDefault = false, UseYn = true, WarehouseType = WarehouseTypes.Normal });
        saved.Single(w => w.WarehouseCode == "T01B").UpdatedAt.Should().BeAfter(b.UpdatedAt);
    }

    [DatabaseFact, Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public async Task Save_ANewWarehouse_GetsItsSystemLocationStar_AndADeleteTakesItAlong()
    {
        var added = (await Service.SaveAsync(Batch(added: [New("T712")]), "kim", None)).Single(w => w.WarehouseCode == "T712");

        (await QueryAsync<(string Code, bool IsSystem, bool UseYn)>(
            "SELECT location_code, is_system, use_yn FROM bc_location WHERE warehouse_id = @Id", new { added.Id }))
            .Should().Equal(("*", true, true));

        await Service.SaveAsync(Batch(deleted: [new WarehouseKey(added.Id, added.UpdatedAt)]), "kim", None);
        (await QueryAsync<long>("SELECT count(*) FROM bc_location WHERE warehouse_id = @Id", new { added.Id })).Single().Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T02"), Trait("Id", "MM-UC-017-T03"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public async Task Save_RejectsADuplicateCodeIgnoringCase_AndAChangedCode_AndSavesNothing()
    {
        var stored = (await Service.SaveAsync(Batch(added: [New("T02A")]), "kim", None)).Single(w => w.WarehouseCode == "T02A");

        var duplicate = () => Service.SaveAsync(Batch(added: [New("T02X"), New("t02a")]), "kim", None);
        var changed = () => Service.SaveAsync(Batch(updated: [Row(stored) with { WarehouseCode = "T02B" }]), "kim", None);

        (await duplicate.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.InvalidCode);
        (await changed.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.InvalidCode);
        (await Service.SearchAsync(includeInactive: true, None)).Should().NotContain(w => w.WarehouseCode == "T02X" || w.WarehouseCode == "T02B");
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T04"), Trait("Id", "MM-UT-702"), Trait("Rule", "MM-BR-702")]
    public async Task Save_MakingAnotherRowTheDefault_SwitchesTheOldDefaultOff_AndTwoDefaultsAreRefused()
    {
        var all = await Service.SaveAsync(Batch(added: [New("T04A"), New("T04B")]), "kim", None);
        var oldDefault = all.Single(w => w.IsDefault);
        var (a, b) = (all.Single(w => w.WarehouseCode == "T04A"), all.Single(w => w.WarehouseCode == "T04B"));

        var two = () => Service.SaveAsync(Batch(updated: [Row(a) with { IsDefault = true }, Row(b) with { IsDefault = true }]), "kim", None);
        (await two.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.DefaultCode);

        var moved = await Service.SaveAsync(Batch(updated: [Row(a) with { IsDefault = true }]), "kim", None);
        moved.Where(w => w.IsDefault).Select(w => w.WarehouseCode).Should().Equal("T04A");

        var back = moved.Single(w => w.Id == oldDefault.Id);
        var restored = await Service.SaveAsync(Batch(updated: [Row(back) with { IsDefault = true }]), "kim", None);
        restored.Where(w => w.IsDefault).Select(w => w.Id).Should().Equal(oldDefault.Id);
    }

    [DatabaseFact, Trait("Id", "MM-UT-702"), Trait("Rule", "MM-BR-702")]
    public async Task Save_TheDefaultWarehouse_CannotBeSetNotInUseOrDeleted()
    {
        var main = (await Service.SearchAsync(includeInactive: true, None)).Single(w => w.IsDefault);

        var inactive = () => Service.SaveAsync(Batch(updated: [Row(main) with { UseYn = false }]), "kim", None);
        var delete = () => Service.SaveAsync(Batch(deleted: [new WarehouseKey(main.Id, main.UpdatedAt)]), "kim", None);

        (await inactive.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.DefaultCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.DefaultCode);
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T05"), Trait("Id", "MM-UT-704"), Trait("Rule", "MM-BR-704")]
    public async Task Save_AWarehouseWithStock_CannotBeSetNotInUse()
    {
        var warehouse = await AddAsync("T05A");
        await PostAsync(new("GR", Today, await ItemAsync("ZZT05"), warehouse.Id, 3, Price: 1));
        warehouse = await ReadAsync(warehouse.Id);

        var act = () => Service.SaveAsync(Batch(updated: [Row(warehouse) with { UseYn = false }]), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(WarehouseRules.InUseCode);
        error.Args.Should().Equal("T05A", WarehouseBlockReasons.OnHand);
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T06"), Trait("Id", "MM-UT-704"), Trait("Rule", "MM-BR-704")]
    public async Task Save_AWarehouseWithAMovementAfterToday_CannotBeSetNotInUse_ButWithoutOneItCan()
    {
        var future = await AddAsync("T06A");
        var empty = await AddAsync("T06B");
        await PostAsync(new("GR", Today.AddDays(5), await ItemAsync("ZZT06"), future.Id, 2, Price: 1));
        await ExecuteAsync("UPDATE mm_onhand SET onhand_qty = 0 WHERE warehouse_id = @Id", new { future.Id });
        future = await ReadAsync(future.Id);

        var act = () => Service.SaveAsync(Batch(updated: [Row(future) with { UseYn = false }]), "kim", None);
        var saved = await Service.SaveAsync(Batch(updated: [Row(empty) with { UseYn = false }]), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("T06A", WarehouseBlockReasons.FutureMovement);
        saved.Single(w => w.WarehouseCode == "T06B").UseYn.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T07"), Trait("Id", "MM-UT-704"), Trait("Rule", "MM-BR-704")]
    public async Task Save_AWarehouseWithMovementsOrStockRows_CannotBeDeleted()
    {
        var moved = await AddAsync("T07A");
        var used = await AddAsync("T07B");
        var item = await ItemAsync("ZZT07");
        await PostAsync(new("GR", Today.AddDays(-3), item, moved.Id, 1, Price: 1));
        await ExecuteAsync(StarKeys.Ensure("@Id", "@Item"), new { Item = item, used.Id });
        await ExecuteAsync(
            $"""
            INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
            VALUES (@Item, @Id, {StarKeys.Location("@Id")}, {StarKeys.Lot("@Item")}, 0, 't', 't')
            """,
            new { Item = item, used.Id });
        moved = await ReadAsync(moved.Id);

        var deleteMoved = () => Service.SaveAsync(Batch(deleted: [new WarehouseKey(moved.Id, moved.UpdatedAt)]), "kim", None);
        var deleteUsed = () => Service.SaveAsync(Batch(deleted: [new WarehouseKey(used.Id, used.UpdatedAt)]), "kim", None);

        (await deleteMoved.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("T07A", WarehouseBlockReasons.HasMovement);
        (await deleteUsed.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("T07B", WarehouseBlockReasons.InUse);
    }

    [DatabaseFact, Trait("Id", "MM-UT-704"), Trait("Rule", "MM-BR-704"), Trait("Rule", "MM-BR-703")]
    public async Task Save_DeletingAnUnusedWarehouse_ClearsItAsAUserDefault()
    {
        var warehouse = await AddAsync("T07C");
        var userId = await UserWithDefaultAsync("t07user", warehouse.Id);

        var saved = await Service.SaveAsync(Batch(deleted: [new WarehouseKey(warehouse.Id, warehouse.UpdatedAt)]), "kim", None);

        saved.Should().NotContain(w => w.Id == warehouse.Id);
        (await QueryAsync<long?>("SELECT default_warehouse_id FROM sys_app_user WHERE id = @Id", new { Id = userId })).Single().Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T09"), Trait("Id", "MM-UT-703"), Trait("Rule", "MM-BR-703")]
    public async Task Login_AUserDefaultSetNotInUse_FallsBackToTheCompanyDefault()
    {
        var warehouse = await AddAsync("T09A");
        var userId = await UserWithDefaultAsync("t09user", warehouse.Id);
        (await DefaultWarehouseOfAsync(userId))!.WarehouseCode.Should().Be("T09A");

        await Service.SaveAsync(Batch(updated: [Row(warehouse) with { UseYn = false }]), "kim", None);

        var company = (await Service.SearchAsync(includeInactive: false, None)).Single(w => w.IsDefault);
        (await DefaultWarehouseOfAsync(userId))!.Id.Should().Be(company.Id);
    }

    [DatabaseFact, Trait("Id", "MM-UC-017-T10")]
    public async Task OptionsAndSearch_ShowWarehousesNotInUse_OnlyWithIncludeInactive()
    {
        var warehouse = await AddAsync("T10A");
        await Service.SaveAsync(Batch(updated: [Row(warehouse) with { UseYn = false }]), "kim", None);

        (await Service.GetOptionsAsync(includeConsignment: false, includeInactive: false, None)).Should().NotContain(o => o.WarehouseCode == "T10A");
        (await Service.GetOptionsAsync(includeConsignment: false, includeInactive: true, None))
            .Should().ContainSingle(o => o.WarehouseCode == "T10A").Which.UseYn.Should().BeFalse();
        (await Service.SearchAsync(includeInactive: false, None)).Should().NotContain(w => w.WarehouseCode == "T10A");
        (await Service.SearchAsync(includeInactive: true, None)).Should().Contain(w => w.WarehouseCode == "T10A");
    }

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public async Task Save_TheConsignmentWarehouse_CannotBeDeletedOrSetNotInUse()
    {
        var consignment = (await Service.SearchAsync(includeInactive: true, None)).Single(w => w.WarehouseType == WarehouseTypes.Consignment);

        var delete = () => Service.SaveAsync(Batch(deleted: [new WarehouseKey(consignment.Id, consignment.UpdatedAt)]), "kim", None);
        var inactive = () => Service.SaveAsync(Batch(updated: [Row(consignment) with { UseYn = false }]), "kim", None);

        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.ConsignmentCode);
        (await inactive.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(WarehouseRules.ConsignmentCode);
    }

    [DatabaseFact]
    public async Task Save_ARowChangedAfterItWasRead_IsAConcurrencyError()
    {
        var warehouse = await AddAsync("T11A");
        await Service.SaveAsync(Batch(updated: [Row(warehouse) with { WarehouseName = "First" }]), "kim", None);

        var act = () => Service.SaveAsync(Batch(updated: [Row(warehouse) with { WarehouseName = "Second" }]), "lee", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private static WarehouseSaveRow New(string code) => new(null, code, "Name " + code.Trim(), false, true, null, null, null);

    private static WarehouseSaveRow Row(WarehouseDto w) =>
        new(w.Id, w.WarehouseCode, w.WarehouseName, w.IsDefault, w.UseYn, w.WarehouseType, w.Remarks, w.UpdatedAt);

    private static BatchRequest<WarehouseSaveRow, WarehouseKey> Batch(
        IReadOnlyList<WarehouseSaveRow>? added = null, IReadOnlyList<WarehouseSaveRow>? updated = null, IReadOnlyList<WarehouseKey>? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private async Task<WarehouseDto> AddAsync(string code) =>
        (await Service.SaveAsync(Batch(added: [New(code)]), "kim", None)).Single(w => w.WarehouseCode == code);

    private async Task<WarehouseDto> ReadAsync(long id) => (await Service.SearchAsync(includeInactive: true, None)).Single(w => w.Id == id);

    private async Task<long> ItemAsync(string code) =>
        (await QueryAsync<long>(
            "INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by) VALUES (@Code, 'Item', 'INVENTORY', 't', 't') RETURNING id",
            new { Code = code })).Single();

    private async Task<long> UserWithDefaultAsync(string userId, long warehouseId)
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, userId, "Pass-1", None);
        return (await QueryAsync<long>(
            "UPDATE sys_app_user SET default_warehouse_id = @Warehouse WHERE userid = @UserId RETURNING id",
            new { Warehouse = warehouseId, UserId = userId })).Single();
    }

    private async Task<AuthWarehouse?> DefaultWarehouseOfAsync(long appUserId)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        return await db.Get<IAppUserRepository>().FindDefaultWarehouseAsync(appUserId, uow.Transaction, None);
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

        public DateOnly Today() => WarehouseServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

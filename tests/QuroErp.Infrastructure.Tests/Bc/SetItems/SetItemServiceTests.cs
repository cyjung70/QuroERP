using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.SetItems;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SetItems;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.SetItems;

/// <summary>Own schema for set item tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class SetItemSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "SetItemSchema";
}

/// <summary>
/// SetItemService with the Dapper repository on real PostgreSQL (BC-UC-013, SD-BR-501~508, MM-BR-361). Each test makes its own
/// set and items.
/// </summary>
[Collection(SetItemSchemaDefinition.Name)]
public class SetItemServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private SetItemService Service => new(db.Get<ISetItemRepository>(), db.Get<ILocalizationRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "BC-UC-013-T01"), Trait("Id", "SD-UT-505")]
    public async Task Batch_AddsUpdatesAndDeletes_AndNumbersNewRowsAfterTheLargestSequence()
    {
        var m = await MastersAsync();
        var first = await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1), Row(m.B, 2)]), "kim", None);
        var a = first.Components.Single(c => c.ItemCd == m.A);
        var b = first.Components.Single(c => c.ItemCd == m.B);

        var saved = await Service.SaveAsync(
            m.Set, Batch(added: [Row(m.C, 4)], updated: [new(a.Id, m.A, 3, a.UpdatedAt)], deleted: [new(b.Id, b.UpdatedAt)]), "lee", None);

        first.Components.Select(c => (c.Seq, c.ItemCd)).Should().Equal((1, m.A), (2, m.B));
        saved.Components.Select(c => (c.Seq, c.ItemCd, c.Qty)).Should().Equal((1, m.A, 3m), (3, m.C, 4m));
        saved.Components[0].Unit.Should().Be("EA");
        (saved.ItemCd, saved.ItemName).Should().Be((m.Set, "Kit"));
    }

    // SD-BR-501: a Set has components as well as a Kit; MM-BR-361: a lot-managed item can be one of them
    [DatabaseFact, Trait("Id", "SD-UT-501"), Trait("Id", "MM-UT-361")]
    public async Task Batch_ASetToo_AndALotManagedComponent_AreSaved()
    {
        var m = await MastersAsync();

        var saved = await Service.SaveAsync(m.Other, Batch(added: [Row(m.A, 1), Row(m.Lot, 2)]), "kim", None);

        saved.Components.Select(c => (c.ItemCd, c.Qty)).Should().Equal((m.A, 1m), (m.Lot, 2m));
    }

    [DatabaseFact, Trait("Id", "BC-UC-013-T02"), Trait("Id", "SD-UT-506"), Trait("Id", "SD-UT-502"), Trait("Id", "SD-UT-503")]
    public async Task Batch_OneBadRow_SavesNothing()
    {
        var m = await MastersAsync();

        foreach (var (row, code) in new[]
        {
            (Row(m.Service, 1), SetItemRules.ComponentCode),
            (Row(m.Unused, 1), SetItemRules.ComponentCode),
            (Row(m.Other, 1), SetItemRules.ComponentCode),
            (Row(m.B, 0), SetItemRules.QtyCode),
            (Row(m.B, 1.23456m), DecimalPolicy.TooManyDecimalsCode),
        })
        {
            var act = () => Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1), row]), "kim", None);

            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(code, row.ItemCd);
        }
        (await Service.GetAsync(m.Set, None)).Components.Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UC-013-T03"), Trait("Id", "SD-UT-501")]
    public async Task Get_FindsTheSetInAnyCase_AndRefusesAnItemThatIsNotASetInUse()
    {
        var m = await MastersAsync();
        await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1)]), "kim", None);

        var found = await Service.GetAsync(" " + m.Set.ToLowerInvariant(), None);

        found.Components.Select(c => c.ItemCd).Should().Equal(m.A);
        foreach (var code in new[] { m.A, m.OffSet, "NO-SUCH-ITEM" })
        {
            var act = () => Service.GetAsync(code, None);
            (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(code);
        }
        var save = () => Service.SaveAsync(m.OffSet, Batch(added: [Row(m.A, 1)]), "kim", None);
        (await save.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SetItemRules.SetCode);
    }

    [DatabaseFact, Trait("Id", "SD-UT-504"), Trait("Rule", "SD-BR-504")]
    public async Task Batch_ComponentTwice_IsSdE504_ButDeleteAndAddAgainAndSwappingWork()
    {
        var m = await MastersAsync();
        var first = await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1), Row(m.B, 2)]), "kim", None);
        var a = first.Components[0];
        var b = first.Components[1];

        var twice = () => Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 5)]), "kim", None);
        (await twice.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(m.A);

        var again = await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 5)], deleted: [new(a.Id, a.UpdatedAt)]), "kim", None);
        var newA = again.Components.Single(c => c.ItemCd == m.A);
        var swapped = await Service.SaveAsync(
            m.Set, Batch(updated: [new(b.Id, m.A, 2, b.UpdatedAt), new(newA.Id, m.B, 5, newA.UpdatedAt)]), "kim", None);

        swapped.Components.Select(c => (c.Seq, c.ItemCd, c.Qty)).Should().Equal((2, m.A, 2m), (3, m.B, 5m));
    }

    [DatabaseFact, Trait("Id", "SD-UT-506")]
    public async Task Batch_RowReadBeforeAnotherSave_OrOfAnotherSet_Is409()
    {
        var m = await MastersAsync();
        var other = await MastersAsync();
        var a = (await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1)]), "kim", None)).Components[0];
        var foreign = (await Service.SaveAsync(other.Set, Batch(added: [Row(other.A, 1)]), "kim", None)).Components[0];
        await Service.SaveAsync(m.Set, Batch(updated: [new(a.Id, m.A, 2, a.UpdatedAt)]), "lee", None);

        var stale = () => Service.SaveAsync(m.Set, Batch(updated: [new(a.Id, m.A, 3, a.UpdatedAt)]), "kim", None);
        var wrongSet = () => Service.SaveAsync(m.Set, Batch(deleted: [new(foreign.Id, foreign.UpdatedAt)]), "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        await wrongSet.Should().ThrowAsync<ConcurrencyException>();
        (await Service.GetAsync(other.Set, None)).Components.Should().ContainSingle();
    }

    [DatabaseFact, Trait("Id", "SD-UT-507"), Trait("Rule", "SD-BR-507")]
    public async Task ChangedComposition_IsUsedByTheNextSetMovement_AndEarlierMovementsStay()
    {
        var m = await MastersAsync();
        var warehouse = (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", null)).Single();
        var ids = await IdsAsync(m.Set, m.A, m.B);
        await PostAsync(
            new StockMovementRequest("GR", Day, ids[m.A], warehouse, 10, Price: 1),
            new StockMovementRequest("GR", Day, ids[m.B], warehouse, 10, Price: 1));
        var row = (await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 2)]), "kim", None)).Components[0];
        await PostAsync(new StockMovementRequest("DN", Day, ids[m.Set], warehouse, -1));

        await Service.SaveAsync(m.Set, Batch(updated: [new(row.Id, m.B, 3, row.UpdatedAt)]), "kim", None);
        await PostAsync(new StockMovementRequest("DN", Day, ids[m.Set], warehouse, -1));

        (await QueryAsync<(long ItemId, decimal Qty)>(
                "SELECT item_id, qty FROM mm_goods_movement WHERE set_item_id = @Set ORDER BY id", new { Set = ids[m.Set] }))
            .Should().Equal((ids[m.A], -2m), (ids[m.B], -3m));
    }

    [DatabaseFact, Trait("Id", "SD-UT-509"), Trait("Rule", "SD-BR-509")]
    public async Task SetWithAllComponentsDeleted_CannotBeMoved()
    {
        var m = await MastersAsync();
        var warehouse = (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", null)).Single();
        var row = (await Service.SaveAsync(m.Set, Batch(added: [Row(m.A, 1)]), "kim", None)).Components[0];
        await Service.SaveAsync(m.Set, Batch(deleted: [new(row.Id, row.UpdatedAt)]), "kim", None);
        var setId = (await IdsAsync(m.Set))[m.Set];

        var act = () => PostAsync(new StockMovementRequest("DN", Day, setId, warehouse, -1));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-509");
    }

    [DatabaseFact, Trait("Id", "SD-UT-508"), Trait("Rule", "SD-BR-508")]
    public async Task CopyingAnotherSet_SavesItsComponentsAsNewRows()
    {
        var m = await MastersAsync();
        var source = await MastersAsync();
        await Service.SaveAsync(source.Set, Batch(added: [Row(m.A, 1), Row(m.B, 2)]), "kim", None);

        var copied = (await Service.GetAsync(source.Set, None)).Components.Select(c => Row(c.ItemCd, c.Qty)).ToArray();
        var saved = await Service.SaveAsync(m.Set, Batch(added: copied), "kim", None);

        saved.Components.Select(c => (c.Seq, c.ItemCd, c.Qty)).Should().Equal((1, m.A, 1m), (2, m.B, 2m));
    }

    private static readonly DateOnly Day = new(2026, 10, 10);

    private static SetComponentRowRequest Row(string itemCd, decimal qty) => new(null, itemCd, qty, null);

    private static BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> Batch(
        SetComponentRowRequest[]? added = null, SetComponentRowRequest[]? updated = null, SetComponentDeleteRequest[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private sealed record Masters(string Set, string OffSet, string A, string B, string C, string Service, string Unused, string Other, string Lot);

    // A set in use and one not in use, three inventory items, a service item, an unused item, another set and a lot-managed item
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var m = new Masters("K" + key, "O" + key, "A" + key, "B" + key, "C" + key, "S" + key, "U" + key, "X" + key, "L" + key);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, lot_managed, created_by, updated_by) VALUES
                (@Set, 'Kit', 'KIT', 'EA', 'Y', false, 't', 't'),
                (@OffSet, 'Old kit', 'KIT', 'EA', 'N', false, 't', 't'),
                (@A, 'Bolt', 'INVENTORY', 'EA', 'Y', false, 't', 't'),
                (@B, 'Nut', 'INVENTORY', 'EA', NULL, false, 't', 't'),
                (@C, 'Washer', 'INVENTORY', 'EA', 'Y', false, 't', 't'),
                (@Service, 'Freight', 'SERVICE', 'EA', 'Y', false, 't', 't'),
                (@Unused, 'Old bolt', 'INVENTORY', 'EA', 'N', false, 't', 't'),
                (@Other, 'Other set', 'SET', 'EA', 'Y', false, 't', 't'),
                (@Lot, 'Lot bolt', 'INVENTORY', 'EA', 'Y', true, 't', 't')
            """,
            m);
        return m;
    }

    private async Task<Dictionary<string, long>> IdsAsync(params string[] codes) =>
        (await QueryAsync<(string ItemCd, long Id)>("SELECT item_cd, id FROM bc_item WHERE item_cd = ANY(@codes)", new { codes }))
        .ToDictionary(r => r.ItemCd, r => r.Id);

    private async Task PostAsync(params StockMovementRequest[] requests)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await new InventoryService(
                db.Get<IInventoryRepository>(),
                db.Get<IInventorySettingRepository>(),
                db.Get<ISetComponentRepository>(),
                new DocumentNumberService(db.Get<IDocumentNumberRepository>()))
            .PostAsync(requests, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }
}

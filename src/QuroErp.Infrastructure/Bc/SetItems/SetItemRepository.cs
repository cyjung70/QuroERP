using System.Data;
using Dapper;
using QuroErp.Application.Bc.SetItems;
using QuroErp.Domain.Bc.SetItems;

namespace QuroErp.Infrastructure.Bc.SetItems;

internal sealed class SetItemRepository : ISetItemRepository
{
    private const string ItemColumns = "id, item_cd, item_name, type AS item_type, valid_flag, lot_managed";

    private const string SelectItem = "SELECT " + ItemColumns + " FROM bc_item WHERE item_cd = @ItemCd";

    // The same row lock the inventory service takes before expanding the set (inventory.md step 0)
    private const string SelectItemForUpdate = SelectItem + " FOR UPDATE";

    private const string SelectItems = "SELECT " + ItemColumns + " FROM bc_item WHERE item_cd = ANY(@ItemCds)";

    private const string SelectComponents = """
        SELECT b.id, b.seq, b.child_item_id AS item_id, i.item_cd, i.item_name, i.unit, b.child_qty AS qty, b.updated_at
        FROM sd_bom b
        JOIN bc_item i ON i.id = b.child_item_id
        WHERE b.parent_item_id = @SetItemId
        ORDER BY b.seq
        """;

    private const string Insert = """
        INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by)
        VALUES (@SetItemId, @Seq, @ComponentItemId, @Qty, @By, @By)
        """;

    private const string Update = """
        UPDATE sd_bom
        SET child_item_id = @ComponentItemId, child_qty = @Qty, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM sd_bom WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    public Task<SetItemCandidate?> FindItemAsync(string itemCd, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<SetItemCandidate?>(new CommandDefinition(
            forUpdate ? SelectItemForUpdate : SelectItem, new { ItemCd = itemCd }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<SetItemCandidate>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        itemCds.Count == 0
            ? []
            : (await transaction.Connection!.QueryAsync<SetItemCandidate>(new CommandDefinition(
                SelectItems, new { ItemCds = itemCds.ToArray() }, transaction, cancellationToken: cancellationToken))).ToList();

    public async Task<IReadOnlyList<SetComponentRow>> FindComponentsAsync(
        long setItemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, int Seq, long ItemId, string ItemCd, string? ItemName, string? Unit, decimal Qty, DateTime UpdatedAt)>(
            new CommandDefinition(SelectComponents, new { SetItemId = setItemId }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new SetComponentRow(r.Id, r.Seq, r.ItemId, r.ItemCd, r.ItemName, r.Unit, r.Qty, Utc(r.UpdatedAt)))
        .ToList();

    public Task InsertAsync(
        long setItemId, int seq, long componentItemId, decimal qty, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert,
            new { SetItemId = setItemId, Seq = seq, ComponentItemId = componentItemId, Qty = qty, By = user },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, long componentItemId, decimal qty, DateTimeOffset updatedAt, string user, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { Id = id, ComponentItemId = componentItemId, Qty = qty, By = user, ExpectedUpdatedAt = updatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, ExpectedUpdatedAt = updatedAt.UtcDateTime }, transaction, cancellationToken: cancellationToken)) == 1;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

using System.Data;
using Dapper;
using QuroErp.Application.Bc.PurchasePrices;
using QuroErp.Domain.Bc.PurchasePrices;

namespace QuroErp.Infrastructure.Bc.PurchasePrices;

internal sealed class PurchasePriceRepository : IPurchasePriceRepository
{
    // A transaction-scoped advisory lock per price group: a group has no parent row to lock
    private const string LockGroup = "SELECT pg_advisory_xact_lock(hashtext('bc_purchase_price:' || @VendorPrice))";

    private const string SelectByGroup = """
        SELECT p.id, p.item_id, i.item_cd, i.item_name, p.purchase_prc, i.usd_prc AS std_prc, p.updated_at
        FROM bc_purchase_price p
        JOIN bc_item i ON i.id = p.item_id
        WHERE p.vendor_price = @VendorPrice
        ORDER BY i.item_cd
        """;

    private const string SelectItems = "SELECT id, item_cd, type AS item_type, valid_flag FROM bc_item WHERE item_cd = ANY(@ItemCds)";

    private const string Insert = """
        INSERT INTO bc_purchase_price (vendor_price, item_id, purchase_prc, created_by, updated_by)
        VALUES (@VendorPrice, @ItemId, @PurchasePrc, @By, @By)
        """;

    private const string Update = """
        UPDATE bc_purchase_price
        SET item_id = @ItemId, purchase_prc = @PurchasePrc, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM bc_purchase_price WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    public Task LockGroupAsync(string vendorPrice, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            LockGroup, new { VendorPrice = vendorPrice }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<PurchasePriceRow>> FindByGroupAsync(
        string vendorPrice, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, long ItemId, string ItemCd, string? ItemName, decimal PurchasePrc, decimal? StdPrc, DateTime UpdatedAt)>(
            new CommandDefinition(SelectByGroup, new { VendorPrice = vendorPrice }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new PurchasePriceRow(r.Id, r.ItemId, r.ItemCd, r.ItemName, r.PurchasePrc, r.StdPrc, Utc(r.UpdatedAt)))
        .ToList();

    public async Task<IReadOnlyList<PurchasePriceItem>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        itemCds.Count == 0
            ? []
            : (await transaction.Connection!.QueryAsync<(long Id, string ItemCd, string? ItemType, string? ValidFlag)>(
                new CommandDefinition(SelectItems, new { ItemCds = itemCds.ToArray() }, transaction, cancellationToken: cancellationToken)))
            .Select(r => new PurchasePriceItem(r.Id, r.ItemCd, r.ItemType, r.ValidFlag))
            .ToList();

    public Task InsertAsync(string vendorPrice, PurchasePriceWrite row, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert, new { VendorPrice = vendorPrice, row.ItemId, row.PurchasePrc, By = user }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        PurchasePriceWrite row, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { row.Id, row.ItemId, row.PurchasePrc, By = user, ExpectedUpdatedAt = updatedAt.UtcDateTime },
            transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, ExpectedUpdatedAt = updatedAt.UtcDateTime }, transaction, cancellationToken: cancellationToken)) == 1;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

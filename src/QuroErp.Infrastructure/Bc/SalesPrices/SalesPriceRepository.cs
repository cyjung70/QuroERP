using System.Data;
using Dapper;
using QuroErp.Application.Bc.SalesPrices;
using QuroErp.Domain.Bc.SalesPrices;

namespace QuroErp.Infrastructure.Bc.SalesPrices;

internal sealed class SalesPriceRepository : ISalesPriceRepository
{
    // A transaction-scoped advisory lock per price level: a level has no parent row to lock
    private const string LockLevel = "SELECT pg_advisory_xact_lock(hashtext('bc_sales_price:' || @PriceLevel))";

    private const string SelectByLevel = """
        SELECT p.id, p.item_id, i.item_cd, i.item_name, p.sales_prc, i.so_usd_prc AS std_prc, p.updated_at
        FROM bc_sales_price p
        JOIN bc_item i ON i.id = p.item_id
        WHERE p.price_level = @PriceLevel
        ORDER BY i.item_cd
        """;

    private const string SelectItems = "SELECT id, item_cd, type AS item_type, valid_flag FROM bc_item WHERE item_cd = ANY(@ItemCds)";

    private const string Insert = """
        INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by)
        VALUES (@PriceLevel, @ItemId, @SalesPrc, @By, @By)
        """;

    private const string Update = """
        UPDATE bc_sales_price
        SET item_id = @ItemId, sales_prc = @SalesPrc, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM bc_sales_price WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    public Task LockLevelAsync(string priceLevel, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            LockLevel, new { PriceLevel = priceLevel }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<SalesPriceRow>> FindByLevelAsync(
        string priceLevel, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, long ItemId, string ItemCd, string? ItemName, decimal SalesPrc, decimal? StdPrc, DateTime UpdatedAt)>(
            new CommandDefinition(SelectByLevel, new { PriceLevel = priceLevel }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new SalesPriceRow(r.Id, r.ItemId, r.ItemCd, r.ItemName, r.SalesPrc, r.StdPrc, Utc(r.UpdatedAt)))
        .ToList();

    public async Task<IReadOnlyList<SalesPriceItem>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        itemCds.Count == 0
            ? []
            : (await transaction.Connection!.QueryAsync<(long Id, string ItemCd, string? ItemType, string? ValidFlag)>(
                new CommandDefinition(SelectItems, new { ItemCds = itemCds.ToArray() }, transaction, cancellationToken: cancellationToken)))
            .Select(r => new SalesPriceItem(r.Id, r.ItemCd, r.ItemType, r.ValidFlag))
            .ToList();

    public Task InsertAsync(string priceLevel, SalesPriceWrite row, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert, new { PriceLevel = priceLevel, row.ItemId, row.SalesPrc, By = user }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        SalesPriceWrite row, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { row.Id, row.ItemId, row.SalesPrc, By = user, ExpectedUpdatedAt = updatedAt.UtcDateTime },
            transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, ExpectedUpdatedAt = updatedAt.UtcDateTime }, transaction, cancellationToken: cancellationToken)) == 1;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

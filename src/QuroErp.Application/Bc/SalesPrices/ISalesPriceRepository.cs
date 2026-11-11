using System.Data;
using QuroErp.Domain.Bc.SalesPrices;

namespace QuroErp.Application.Bc.SalesPrices;

/// <summary>bc_sales_price row with its item code, name and standard sales price (bc_item.so_usd_prc).</summary>
public sealed record SalesPriceRow(
    long Id, long ItemId, string ItemCd, string? ItemName, decimal SalesPrc, decimal? StdPrc, DateTimeOffset UpdatedAt);

/// <summary>A sales price to insert or update.</summary>
public sealed record SalesPriceWrite(long? Id, long ItemId, decimal SalesPrc);

/// <summary>Sales price table (bc_sales_price). Callers pass their own transaction.</summary>
public interface ISalesPriceRepository
{
    /// <summary>Serializes saves of one price level until the transaction ends, so two saves cannot add the same item.</summary>
    Task LockLevelAsync(string priceLevel, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Rows of the price level in item code order.</summary>
    Task<IReadOnlyList<SalesPriceRow>> FindByLevelAsync(string priceLevel, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items with these codes; codes that do not exist are left out.</summary>
    Task<IReadOnlyList<SalesPriceItem>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertAsync(string priceLevel, SalesPriceWrite row, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> UpdateAsync(
        SalesPriceWrite row, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}

using System.Data;
using QuroErp.Domain.Bc.PurchasePrices;

namespace QuroErp.Application.Bc.PurchasePrices;

/// <summary>bc_purchase_price row with its item code, name and standard purchase price (bc_item.usd_prc).</summary>
public sealed record PurchasePriceRow(
    long Id, long ItemId, string ItemCd, string? ItemName, decimal PurchasePrc, decimal? StdPrc, DateTimeOffset UpdatedAt);

/// <summary>A purchase price to insert or update.</summary>
public sealed record PurchasePriceWrite(long? Id, long ItemId, decimal PurchasePrc);

/// <summary>Purchase price table (bc_purchase_price). Callers pass their own transaction.</summary>
public interface IPurchasePriceRepository
{
    /// <summary>Serializes saves of one price group until the transaction ends, so two saves cannot add the same item.</summary>
    Task LockGroupAsync(string vendorPrice, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Rows of the price group in item code order.</summary>
    Task<IReadOnlyList<PurchasePriceRow>> FindByGroupAsync(string vendorPrice, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items with these codes; codes that do not exist are left out.</summary>
    Task<IReadOnlyList<PurchasePriceItem>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertAsync(string vendorPrice, PurchasePriceWrite row, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> UpdateAsync(
        PurchasePriceWrite row, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}

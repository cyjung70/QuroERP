using System.Data;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Bc.Items;

/// <summary>A bc_item row. Numbers the legacy data left empty are null.</summary>
public sealed record ItemRow(
    long Id,
    string ItemCd,
    string? ItemName,
    string? ItemName2,
    string? ItemDesc,
    string? ItemType,
    string? Unit,
    string? ItemClass,
    string? ValidFlag,
    decimal? UsdPrc,
    decimal? SoUsdPrc,
    string? UpcCode,
    string? Location,
    decimal? ReorderPoint,
    string? Memo1,
    string? Memo2,
    string? Memo3,
    string? Memo4,
    string? Memo5,
    string? ItemGrade,
    bool Taxable,
    string VatKind,
    string? Plating,
    string? ItemGroupCd,
    decimal? MvgCost,
    decimal? OnhandQty,
    decimal? StkVal,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>List conditions as given by the caller; the repository escapes LIKE wildcards.</summary>
public sealed record ItemSearch(string? ItemCd, string? ItemName, string? ItemClass, string? ValidFlag, int Offset, int Limit);

/// <summary>An item of the barcode screen (BC-UC-011).</summary>
public sealed record ItemBarcodeRow(long Id, string ItemCd, string? ItemName, decimal? SoUsdPrc, string? UpcCode);

/// <summary>An item that already has one of the asked barcodes.</summary>
public sealed record ItemBarcodeOwner(string UpcCode, string ItemCd);

public interface IItemRepository
{
    /// <summary>Items of an item class in item code order (MM-BR-815).</summary>
    /// <param name="validFlag">Y or N; null for all.</param>
    /// <param name="forUpdate">Lock the rows until the transaction ends.</param>
    Task<IReadOnlyList<ItemBarcodeRow>> ListForBarcodeAsync(
        string itemClass, string? validFlag, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items other than exceptIds that have one of these barcodes (MM-BR-817).</summary>
    Task<IReadOnlyList<ItemBarcodeOwner>> FindBarcodeOwnersAsync(
        IReadOnlyCollection<string> upcCodes, IReadOnlyCollection<long> exceptIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the barcodes of these items, changing updated_at so an open item form must read them again.
    /// Barcodes may move between the given items.
    /// </summary>
    Task SaveBarcodesAsync(
        IReadOnlyList<(long Id, string UpcCode)> barcodes, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Lock the row until the transaction ends.</param>
    Task<ItemRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The item before or after the given one in item class (empty first), item code order; null at either end.</summary>
    Task<ItemRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Matching items in item class, item code order, and the number of all matches.</summary>
    Task<(IReadOnlyList<ItemRow> Items, int Total)> SearchAsync(ItemSearch search, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new id.</returns>
    Task<long> InsertAsync(Item item, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the editable values and item group only when updated_at still equals expectedUpdatedAt.</summary>
    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> UpdateAsync(
        Item item, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Stock in any warehouse, stock movements, or set structure as parent or component (MM-BR-806).</summary>
    Task<bool> IsTypeLockedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Used by a document, price, set structure, alternate item group, cart or stock movement (MM-BR-807).</summary>
    Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// The item's own image; when it has none, the image of another item in its item group, plating R or empty first, then by item
    /// code (MM-BR-820, SD-BR-607). Null when the item does not exist or none is found. An empty stored image counts as none.
    /// </summary>
    Task<byte[]?> FindImageAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Replaces the whole stored image, or clears it with null (MM-BR-822, MM-BR-823). updated_at is left alone.</summary>
    /// <returns>False when there is no item with that code.</returns>
    Task<bool> SaveImageAsync(string itemCd, byte[]? image, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}

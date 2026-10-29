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
    DateTimeOffset UpdatedAt);

/// <summary>List conditions as given by the caller; the repository escapes LIKE wildcards.</summary>
public sealed record ItemSearch(string? ItemCd, string? ItemName, string? ItemClass, string? ValidFlag, int Offset, int Limit);

public interface IItemRepository
{
    /// <param name="forUpdate">Lock the row until the transaction ends.</param>
    Task<ItemRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken);

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

    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}

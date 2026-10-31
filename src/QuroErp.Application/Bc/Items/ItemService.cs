using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Items;

/// <summary>The item master screen (BC-UC-009).</summary>
public sealed class ItemService(IItemRepository items, ICodeValidator codes, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The item does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<ItemListDto> SearchAsync(ItemSearchQuery query, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(query.Size, 1, ItemRoutes.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var (rows, total) = await items.SearchAsync(
            new ItemSearch(query.ItemCd, query.ItemName, query.ItemClass, query.ValidFlag, (page - 1) * size, size),
            uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new ItemListDto(rows.Select(ToDto).ToList(), total);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<ItemDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await items.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>The item before or after the given one in item class, item code order (BC-UC-009 basic flow 1); null at either end.</summary>
    public async Task<ItemDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await items.FindAdjacentAsync(id, forward, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return row is null ? null : ToDto(row);
    }

    /// <exception cref="DomainException">MM-E-801 (also for a used code), 802, 803, 825, 831; Common-E-002 for an unknown code value.</exception>
    public async Task<ItemDto> CreateAsync(CreateItemRequest request, string createdBy, CancellationToken cancellationToken)
    {
        var item = Item.Create(request.ItemCd, ToFields(request));
        await EnsureCodesAsync(item.Fields, cancellationToken);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        if (await items.CodeExistsAsync(item.ItemCd, uow.Transaction, cancellationToken))
        {
            throw new DomainException(Item.InvalidCodeCode, item.ItemCd);
        }
        var id = await items.InsertAsync(item, createdBy, uow.Transaction, cancellationToken);
        var saved = await items.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!);
    }

    /// <exception cref="DomainException">MM-E-802, 803, 806, 825, 831; Common-E-002 for an unknown code value.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<ItemDto> UpdateAsync(long id, UpdateItemRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var fields = Item.Normalize(ToFields(request));
        await EnsureCodesAsync(fields, cancellationToken);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await items.FindAsync(id, forUpdate: true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var item = ToItem(row);
        var typeLocked = fields.ItemType != item.Fields.ItemType && await items.IsTypeLockedAsync(id, uow.Transaction, cancellationToken);
        item.Change(fields, typeLocked);

        if (!await items.UpdateAsync(item, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = await items.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!);
    }

    /// <exception cref="DomainException">MM-E-807 when the item is used anywhere.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await items.FindAsync(id, forUpdate: true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != updatedAt)
        {
            throw new ConcurrencyException();
        }
        ToItem(row).EnsureDeletable(await items.IsInUseAsync(id, uow.Transaction, cancellationToken));
        if (!await items.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    private async Task EnsureCodesAsync(ItemFields fields, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.ItemType, fields.ItemType, cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.Unit, fields.Unit, cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ItemClass, fields.ItemClass, cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ValidFlag, fields.ValidFlag, cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.Grade, fields.ItemGrade, cancellationToken);
    }

    private static ItemFields ToFields(ItemFieldsRequest r) => new(
        r.ItemName, r.ItemName2, r.ItemDesc, r.ItemType, r.Unit, r.ItemClass, r.ValidFlag, r.UsdPrc, r.SoUsdPrc, r.UpcCode, r.Location,
        r.ReorderPoint, r.Memo1, r.Memo2, r.Memo3, r.Memo4, r.Memo5, r.ItemGrade, r.Taxable, r.VatKind);

    private static Item ToItem(ItemRow r) => Item.Restore(
        r.Id,
        r.ItemCd,
        new ItemFields(
            r.ItemName, r.ItemName2, r.ItemDesc, r.ItemType, r.Unit, r.ItemClass, r.ValidFlag, r.UsdPrc, r.SoUsdPrc, r.UpcCode, r.Location,
            r.ReorderPoint, r.Memo1, r.Memo2, r.Memo3, r.Memo4, r.Memo5, r.ItemGrade, r.Taxable, r.VatKind),
        r.Plating,
        r.MvgCost ?? 0,
        r.OnhandQty ?? 0,
        r.StkVal ?? 0);

    private static ItemDto ToDto(ItemRow r) => new(
        r.Id, r.ItemCd, r.ItemName, r.ItemName2, r.ItemDesc, r.ItemType, r.Unit, r.ItemClass, r.ValidFlag, r.UsdPrc ?? 0, r.SoUsdPrc ?? 0,
        r.UpcCode, r.Location, r.ReorderPoint ?? 0, r.Memo1, r.Memo2, r.Memo3, r.Memo4, r.Memo5, r.ItemGrade, r.Taxable, r.VatKind,
        r.ItemGroupCd ?? r.ItemCd, r.MvgCost ?? 0, r.OnhandQty ?? 0, r.StkVal ?? 0, r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy);
}

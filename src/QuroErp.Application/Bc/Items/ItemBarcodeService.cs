using System.Data;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Items;

/// <summary>Item barcode list and bulk generation (BC-UC-011, MM-BR-815~818).</summary>
public sealed class ItemBarcodeService(IItemRepository items, ICodeValidator codes, IUnitOfWorkFactory unitOfWork)
{
    /// <exception cref="DomainException">MM-E-802 without an item class; Common-E-002 for an unknown code value.</exception>
    public async Task<IReadOnlyList<ItemBarcodeDto>> ListAsync(ItemBarcodeQuery query, CancellationToken cancellationToken)
    {
        var (itemClass, validFlag) = await SelectionAsync(query.ItemClass, query.ValidFlag, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await items.ListForBarcodeAsync(itemClass, validFlag, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(r => ToDto(r, r.UpcCode)).ToList();
    }

    /// <summary>Counts, new barcodes and conflicts of a run without saving, for the confirmation (MM-BR-815~817).</summary>
    /// <exception cref="DomainException">MM-E-802, MM-E-816; Common-E-002 for an unknown code value.</exception>
    public async Task<ItemBarcodePreviewDto> PreviewAsync(ItemBarcodePreviewQuery query, CancellationToken cancellationToken)
    {
        var (itemClass, validFlag) = await SelectionAsync(query.ItemClass, query.ValidFlag, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var plan = await PlanAsync(itemClass, validFlag, query.StartNo, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new ItemBarcodePreviewDto(
            plan.Rows.Count,
            plan.Rows.Count(r => r.UpcCode is not null),
            plan.Rows.Select((r, i) => ToDto(r, plan.Barcodes[i])).ToList(),
            plan.Conflicts);
    }

    /// <summary>Writes the barcodes of all selected items in one transaction; nothing changes when any conflicts (MM-BR-817, MM-BR-818).</summary>
    /// <exception cref="DomainException">MM-E-802, MM-E-816, MM-E-817 (arg: the other items); Common-E-002.</exception>
    public async Task<GenerateItemBarcodesResultDto> GenerateAsync(
        GenerateItemBarcodesRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var (itemClass, validFlag) = await SelectionAsync(request.ItemClass, request.ValidFlag, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        // The rows are locked, so the result matches what is checked here even if a preview went stale
        var plan = await PlanAsync(itemClass, validFlag, request.StartNo, forUpdate: true, uow.Transaction, cancellationToken);
        if (plan.Conflicts.Count > 0)
        {
            throw ItemBarcode.Duplicate(plan.Conflicts.Select(c => c.OtherItemCd));
        }
        await items.SaveBarcodesAsync(
            plan.Rows.Select((r, i) => (r.Id, plan.Barcodes[i])).ToList(), updatedBy, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new GenerateItemBarcodesResultDto(plan.Rows.Count);
    }

    private async Task<Plan> PlanAsync(
        string itemClass, string? validFlag, string? startNo, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await items.ListForBarcodeAsync(itemClass, validFlag, forUpdate, transaction, cancellationToken);
        var barcodes = ItemBarcode.Generate(string.IsNullOrWhiteSpace(startNo) ? ItemBarcode.DefaultStartNo : startNo, rows.Count);
        // Barcodes of the selected items are overwritten, so only other items can conflict
        IReadOnlyList<ItemBarcodeOwner> owners = rows.Count == 0
            ? []
            : await items.FindBarcodeOwnersAsync(barcodes, rows.Select(r => r.Id).ToList(), transaction, cancellationToken);
        var ownerByBarcode = owners.ToDictionary(o => o.UpcCode, o => o.ItemCd, StringComparer.Ordinal);
        var conflicts = rows
            .Select((r, i) => ownerByBarcode.TryGetValue(barcodes[i], out var other) ? new ItemBarcodeConflictDto(r.ItemCd, barcodes[i], other) : null)
            .OfType<ItemBarcodeConflictDto>()
            .ToList();
        return new Plan(rows, barcodes, conflicts);
    }

    private async Task<(string ItemClass, string? ValidFlag)> SelectionAsync(string? itemClass, string? validFlag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemClass))
        {
            throw new DomainException(Item.RequiredCode, nameof(ItemBarcodeQuery.ItemClass));
        }
        var flag = string.IsNullOrWhiteSpace(validFlag) ? Item.ValidFlagYes : validFlag.Trim();
        await codes.EnsureValidAsync(CodeMajors.ItemClass, itemClass.Trim(), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.DisplayOptions, flag, cancellationToken);
        return (itemClass.Trim(), flag == ItemBarcodeRoutes.AllValidFlags ? null : flag);
    }

    private static ItemBarcodeDto ToDto(ItemBarcodeRow r, string? upcCode) => new(r.Id, r.ItemCd, r.ItemName, r.SoUsdPrc ?? 0, upcCode);

    private sealed record Plan(IReadOnlyList<ItemBarcodeRow> Rows, IReadOnlyList<string> Barcodes, IReadOnlyList<ItemBarcodeConflictDto> Conflicts);
}

using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using DomainOverReceiptModes = QuroErp.Domain.Mm.GoodsReceipts.OverReceiptModes;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>
/// The global inventory setting, changed on the company screen (BC-UC-007): negative stock mode (MM-BR-003,
/// docs/architecture/inventory.md), over-receipt mode (MM-BR-102) and the cost adjustment edit switch (MM-BR-420).
/// </summary>
public sealed class InventorySettingService(IInventorySettingRepository settings, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Name shown as {0} in Common-E-002 for an unknown negative stock mode.</summary>
    public const string ModeName = "NegativeStockMode";

    /// <summary>Name shown as {0} in Common-E-002 for an unknown over-receipt mode.</summary>
    public const string OverReceiptModeName = "OverReceiptMode";

    public async Task<InventorySettingDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await settings.GetAsync(uow.Transaction, cancellationToken) ?? throw MissingRow();
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <exception cref="DomainException">
    /// Common-E-002 when the negative stock mode is not ALLOW_IN_MONTH or DENY, or the over-receipt mode is not DENY or WARN.
    /// </exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored value.</exception>
    public async Task<InventorySettingDto> UpdateAsync(UpdateInventorySettingRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        if (!NegativeStockModeCode.TryParse(request.NegativeStockMode, out var mode))
        {
            throw new DomainException(CodeMessages.InvalidCode, ModeName, request.NegativeStockMode ?? string.Empty);
        }
        if (!DomainOverReceiptModes.All.Contains(request.OverReceiptMode))
        {
            throw new DomainException(CodeMessages.InvalidCode, OverReceiptModeName, request.OverReceiptMode ?? string.Empty);
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var current = await settings.GetAsync(uow.Transaction, cancellationToken) ?? throw MissingRow();
        if (current.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var updated = await settings.UpdateAsync(
                mode.ToCode(), request.OverReceiptMode, request.AllowCostAdjustmentEdit, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken)
            ?? throw new ConcurrencyException();
        await uow.CommitAsync(cancellationToken);
        return ToDto(updated);
    }

    private static InventorySettingDto ToDto(InventorySettingRow row) =>
        new(row.NegativeStockMode, row.OverReceiptMode, row.AllowCostAdjustmentEdit, row.UpdatedAt, row.UpdatedBy);

    // The Schema migration always creates the row, so a missing row is a broken database, not a user error
    private static InvalidOperationException MissingRow() => new("mm_inventory_setting has no row. Apply the Schema migrations.");
}

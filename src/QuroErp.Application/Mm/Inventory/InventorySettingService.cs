using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>The global negative stock mode (MM-BR-003, docs/architecture/inventory.md), changed on the company screen (BC-UC-007).</summary>
public sealed class InventorySettingService(IInventorySettingRepository settings, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Name shown as {0} in Common-E-002 for an unknown mode.</summary>
    public const string ModeName = "NegativeStockMode";

    public async Task<InventorySettingDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await settings.GetAsync(uow.Transaction, cancellationToken) ?? throw MissingRow();
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <exception cref="DomainException">Common-E-002 when the mode is not ALLOW, ALLOW_IN_MONTH or DENY.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored value.</exception>
    public async Task<InventorySettingDto> UpdateAsync(UpdateInventorySettingRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        if (!NegativeStockModeCode.TryParse(request.NegativeStockMode, out var mode))
        {
            throw new DomainException(CodeMessages.InvalidCode, ModeName, request.NegativeStockMode ?? string.Empty);
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var current = await settings.GetAsync(uow.Transaction, cancellationToken) ?? throw MissingRow();
        if (current.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var updated = await settings.UpdateAsync(mode.ToCode(), request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken)
            ?? throw new ConcurrencyException();
        await uow.CommitAsync(cancellationToken);
        return ToDto(updated);
    }

    private static InventorySettingDto ToDto(InventorySettingRow row) => new(row.NegativeStockMode, row.UpdatedAt, row.UpdatedBy);

    // The Schema migration always creates the row, so a missing row is a broken database, not a user error
    private static InvalidOperationException MissingRow() => new("mm_inventory_setting has no row. Apply the Schema migrations.");
}

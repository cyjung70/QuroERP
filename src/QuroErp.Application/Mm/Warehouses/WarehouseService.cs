using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

/// <summary>Warehouse choice list for select fields such as the my info default warehouse (TASK-CORE-022).</summary>
public sealed class WarehouseService(IWarehouseRepository warehouses, IUnitOfWorkFactory unitOfWork)
{
    public async Task<IReadOnlyList<WarehouseOptionDto>> GetOptionsAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var options = await warehouses.FindOptionsAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return options;
    }
}

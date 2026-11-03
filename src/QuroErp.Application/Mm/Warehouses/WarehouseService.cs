using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

/// <summary>Warehouse choice list for select fields such as the my info default warehouse (TASK-CORE-022).</summary>
public sealed class WarehouseService(IWarehouseRepository warehouses, IUnitOfWorkFactory unitOfWork)
{
    /// <param name="includeConsignment">true for report conditions only; document and default warehouse fields leave it out (MM-BR-710).</param>
    public async Task<IReadOnlyList<WarehouseOptionDto>> GetOptionsAsync(bool includeConsignment, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var options = await warehouses.FindOptionsAsync(includeConsignment, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return options;
    }
}

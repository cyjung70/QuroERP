using System.Data;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

public interface IWarehouseRepository
{
    /// <summary>Warehouses in use (use_yn), in warehouse code order. The consignment warehouse only with includeConsignment (MM-BR-710).</summary>
    Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(bool includeConsignment, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The consignment warehouse (MM-BR-709), null when the database has none.</summary>
    Task<ConsignmentWarehouse?> FindConsignmentAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}

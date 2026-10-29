using System.Data;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

public interface IWarehouseRepository
{
    /// <summary>Warehouses in use (use_yn), in warehouse code order.</summary>
    Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}

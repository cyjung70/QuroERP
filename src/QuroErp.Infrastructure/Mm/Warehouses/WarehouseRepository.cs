using System.Data;
using Dapper;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Infrastructure.Mm.Warehouses;

internal sealed class WarehouseRepository : IWarehouseRepository
{
    private const string SelectOptions = """
        SELECT id, warehouse_code, warehouse_name FROM bc_warehouse WHERE use_yn ORDER BY warehouse_code
        """;

    public async Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<WarehouseOptionDto>(
            new CommandDefinition(SelectOptions, transaction: transaction, cancellationToken: cancellationToken))).ToList();
}

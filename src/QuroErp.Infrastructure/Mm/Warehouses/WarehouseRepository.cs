using System.Data;
using Dapper;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Infrastructure.Mm.Warehouses;

internal sealed class WarehouseRepository : IWarehouseRepository
{
    private const string SelectOptions = """
        SELECT id, warehouse_code, warehouse_name, warehouse_type
        FROM bc_warehouse
        WHERE use_yn AND (@IncludeConsignment OR warehouse_type <> 'CONSIGNMENT')
        ORDER BY warehouse_code
        """;

    private const string SelectConsignment = "SELECT id, warehouse_code FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT'";

    public async Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(
        bool includeConsignment, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<WarehouseOptionDto>(new CommandDefinition(
            SelectOptions, new { IncludeConsignment = includeConsignment }, transaction, cancellationToken: cancellationToken))).ToList();

    public Task<ConsignmentWarehouse?> FindConsignmentAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ConsignmentWarehouse?>(
            new CommandDefinition(SelectConsignment, transaction: transaction, cancellationToken: cancellationToken));
}

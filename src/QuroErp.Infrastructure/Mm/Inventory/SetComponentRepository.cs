using System.Data;
using Dapper;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Infrastructure.Mm.Inventory;

internal sealed class SetComponentRepository : ISetComponentRepository
{
    private const string SelectComponents = """
        SELECT parent_item_id AS set_item_id, child_item_id AS component_item_id, child_qty AS component_qty
        FROM sd_bom
        WHERE parent_item_id = ANY(@SetItemIds)
        ORDER BY parent_item_id, seq
        """;

    public async Task<IReadOnlyList<SetComponent>> GetComponentsAsync(
        IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<ComponentRow>(new CommandDefinition(
            SelectComponents, new { SetItemIds = setItemIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new SetComponent(r.SetItemId, r.ComponentItemId, r.ComponentQty)).ToList();
    }

    private sealed record ComponentRow(long SetItemId, long ComponentItemId, decimal ComponentQty);
}

using System.Data;
using Dapper;
using QuroErp.Application.Mm.Labels;

namespace QuroErp.Infrastructure.Mm.Labels;

internal sealed class LabelScanRepository : ILabelScanRepository
{
    public Task<LabelScanItem?> FindItemByBarcodeAsync(string upcCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<LabelScanItem>(new CommandDefinition(
            "SELECT id, item_cd, item_name FROM bc_item WHERE upc_code = @UpcCode ORDER BY id LIMIT 1",
            new { UpcCode = upcCode }, transaction, cancellationToken: cancellationToken));

    public Task<LabelScanItem?> FindItemByCodeAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<LabelScanItem>(new CommandDefinition(
            "SELECT id, item_cd, item_name FROM bc_item WHERE item_cd = @ItemCd",
            new { ItemCd = itemCd }, transaction, cancellationToken: cancellationToken));
}

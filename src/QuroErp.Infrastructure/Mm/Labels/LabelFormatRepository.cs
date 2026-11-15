using System.Data;
using System.Text.Json;
using Dapper;
using QuroErp.Application.Mm.Labels;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Infrastructure.Mm.Labels;

internal sealed class LabelFormatRepository : ILabelFormatRepository
{
    private const string Select = """
        SELECT label_type, output_kind, width_mm, height_mm, dpi, fields::text AS fields
        FROM bc_label_format
        WHERE label_type = @LabelType AND use_yn
        """;

    public async Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Select, new { LabelType = labelType }, transaction, cancellationToken: cancellationToken));
        return row is null
            ? null
            : new LabelFormat(
                row.LabelType, row.OutputKind, row.WidthMm, row.HeightMm, row.Dpi, JsonSerializer.Deserialize<string[]>(row.Fields) ?? []);
    }

    // fields is a jsonb array of field names (MM-BR-917)
    private sealed record Row(string LabelType, string OutputKind, decimal WidthMm, decimal HeightMm, int Dpi, string Fields);
}

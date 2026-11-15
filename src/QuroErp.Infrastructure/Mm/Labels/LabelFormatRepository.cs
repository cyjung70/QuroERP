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

    // Screen order: PALLET, ITEM, LOT, LOCATION (LabelFormatRules.Types)
    private const string SelectAll = """
        SELECT label_type, output_kind, width_mm, height_mm, dpi, fields::text AS fields, use_yn, updated_at
        FROM bc_label_format
        ORDER BY array_position(@Types, label_type::text), label_type
        """;

    private const string Lock = """
        SELECT label_type, output_kind, width_mm, height_mm, dpi, fields::text AS fields, use_yn, updated_at
        FROM bc_label_format
        WHERE label_type = ANY(@LabelTypes)
        ORDER BY label_type
        FOR UPDATE
        """;

    private const string Update = """
        UPDATE bc_label_format
        SET output_kind = @OutputKind, width_mm = @WidthMm, height_mm = @HeightMm, dpi = @Dpi, fields = @Fields::jsonb,
            use_yn = @UseYn, updated_at = now(), updated_by = @UpdatedBy
        WHERE label_type = @LabelType
        """;

    public async Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Select, new { LabelType = labelType }, transaction, cancellationToken: cancellationToken));
        return row is null ? null : ToFormat(row.LabelType, row.OutputKind, row.WidthMm, row.HeightMm, row.Dpi, row.Fields);
    }

    public async Task<IReadOnlyList<LabelFormatRow>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        ToRows(await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            SelectAll, new { Types = LabelFormatRules.Types.ToArray() }, transaction, cancellationToken: cancellationToken)));

    public async Task<IReadOnlyList<LabelFormatRow>> LockAsync(
        IReadOnlyCollection<string> labelTypes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (labelTypes.Count == 0)
        {
            return [];
        }
        return ToRows(await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Lock, new { LabelTypes = labelTypes.ToArray() }, transaction, cancellationToken: cancellationToken)));
    }

    public Task UpdateAsync(LabelFormat format, bool useYn, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                format.LabelType,
                format.OutputKind,
                format.WidthMm,
                format.HeightMm,
                format.Dpi,
                Fields = JsonSerializer.Serialize(format.Fields),
                UseYn = useYn,
                UpdatedBy = updatedBy,
            },
            transaction, cancellationToken: cancellationToken));

    private static LabelFormat ToFormat(string labelType, string outputKind, decimal widthMm, decimal heightMm, int dpi, string fields) =>
        new(labelType, outputKind, widthMm, heightMm, dpi, JsonSerializer.Deserialize<string[]>(fields) ?? []);

    private static List<LabelFormatRow> ToRows(IEnumerable<StoredRow> rows) =>
        rows.Select(r => new LabelFormatRow(
            ToFormat(r.LabelType, r.OutputKind, r.WidthMm, r.HeightMm, r.Dpi, r.Fields), r.UseYn,
            new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();

    // fields is a jsonb array of field names (MM-BR-917)
    private sealed record Row(string LabelType, string OutputKind, decimal WidthMm, decimal HeightMm, int Dpi, string Fields);

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        string LabelType, string OutputKind, decimal WidthMm, decimal HeightMm, int Dpi, string Fields, bool UseYn, DateTime UpdatedAt);
}

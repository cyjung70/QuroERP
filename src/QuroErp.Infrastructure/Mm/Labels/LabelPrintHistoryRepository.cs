using System.Data;
using Dapper;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Infrastructure.Mm.Labels;

internal sealed class LabelPrintHistoryRepository : ILabelPrintHistoryRepository
{
    // The target's text as it is now: Pallet number, item code, Lot number or Location code
    private const string Select = """
        SELECT h.id, h.label_type, h.ref_id,
               CASE h.label_type
                   WHEN 'PALLET' THEN (SELECT x.pallet_no FROM mm_pallet x WHERE x.id = h.ref_id)
                   WHEN 'ITEM' THEN (SELECT x.item_cd FROM bc_item x WHERE x.id = h.ref_id)
                   WHEN 'LOT' THEN (SELECT x.lot_no FROM mm_lot x WHERE x.id = h.ref_id)
                   WHEN 'LOCATION' THEN (SELECT x.location_code FROM bc_location x WHERE x.id = h.ref_id)
               END AS ref_text,
               h.copies, h.output_kind, h.printer_name, h.printed_at, h.printed_by
        FROM mm_label_print h
        WHERE (@From::timestamptz IS NULL OR h.printed_at >= @From)
          AND (@To::timestamptz IS NULL OR h.printed_at < @To)
          AND (@LabelType::varchar IS NULL OR h.label_type = @LabelType)
        ORDER BY h.printed_at DESC, h.id DESC
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<LabelPrintHistoryDto>> FindAsync(
        DateTimeOffset? printedFrom, DateTimeOffset? printedBefore, string? labelType, int limit, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Select,
            new { From = printedFrom?.ToUniversalTime(), To = printedBefore?.ToUniversalTime(), LabelType = labelType, Limit = limit },
            transaction, cancellationToken: cancellationToken));
        return [.. rows.Select(r => new LabelPrintHistoryDto(
            r.Id, r.LabelType, r.RefId, r.RefText, r.Copies, r.OutputKind, r.PrinterName,
            new DateTimeOffset(DateTime.SpecifyKind(r.PrintedAt, DateTimeKind.Utc)), r.PrintedBy))];
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        long Id, string LabelType, long RefId, string? RefText, int Copies, string OutputKind, string? PrinterName, DateTime PrintedAt,
        string PrintedBy);
}

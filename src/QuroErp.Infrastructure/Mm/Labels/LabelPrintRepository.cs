using System.Data;
using Dapper;
using QuroErp.Application.Mm.Labels;

namespace QuroErp.Infrastructure.Mm.Labels;

internal sealed class LabelPrintRepository : ILabelPrintRepository
{
    // MM-BR-919: one row per printed label
    private const string Insert = """
        INSERT INTO mm_label_print (label_type, ref_id, copies, output_kind, printer_name, printed_by, created_by, updated_by)
        VALUES (@LabelType, @RefId, @Copies, @OutputKind, @PrinterName, @PrintedBy, @PrintedBy, @PrintedBy)
        """;

    public Task AddAsync(IReadOnlyList<LabelPrintRow> rows, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Insert, rows, transaction, cancellationToken: cancellationToken));
}

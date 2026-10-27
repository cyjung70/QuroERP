using System.Data;
using Dapper;
using QuroErp.Application.Common.DocumentNumbers;

namespace QuroErp.Infrastructure.Common.DocumentNumbers;

internal sealed class DocumentNumberRepository : IDocumentNumberRepository
{
    private const string LockRow = """
        SELECT trans_no FROM sys_document_number WHERE trans_type = @TransType AND period = @Period FOR UPDATE
        """;

    // ON CONFLICT waits for a transaction inserting the same key and does nothing if that one commits
    private const string InsertRow = """
        INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
        VALUES (@TransType, @Period, @LastNo, @User, @User)
        ON CONFLICT (trans_type, period) DO NOTHING
        """;

    private const string Update = """
        UPDATE sys_document_number
        SET trans_no = @LastNo, updated_at = now(), updated_by = @User
        WHERE trans_type = @TransType AND period = @Period
        """;

    public Task<string?> LockLastAsync(string transType, string period, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            LockRow, new { TransType = transType, Period = period }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> TryCreateAsync(
        string transType, string period, string firstNo, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertRow, new { TransType = transType, Period = period, LastNo = firstNo, User = user }, transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task SaveLastAsync(string transType, string period, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, new { TransType = transType, Period = period, LastNo = lastNo, User = user }, transaction,
            cancellationToken: cancellationToken));
}

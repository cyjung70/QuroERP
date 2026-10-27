using System.Data;
using Dapper;
using QuroErp.Application.Common.DocumentNumbers;

namespace QuroErp.Infrastructure.Common.DocumentNumbers;

internal sealed class DocumentNumberRepository : IDocumentNumberRepository
{
    private const string EnsureRow = """
        INSERT INTO sys_document_number (trans_type, created_by, updated_by)
        VALUES (@TransType, @User, @User)
        ON CONFLICT (trans_type) DO NOTHING
        """;

    private const string LockRow = """
        SELECT trans_no FROM sys_document_number WHERE trans_type = @TransType FOR UPDATE
        """;

    private const string Update = """
        UPDATE sys_document_number
        SET trans_no = @LastNo, updated_at = now(), updated_by = @User
        WHERE trans_type = @TransType
        """;

    public async Task<string?> LockLastAsync(string transType, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            EnsureRow, new { TransType = transType, User = user }, transaction, cancellationToken: cancellationToken));
        return await transaction.Connection!.QuerySingleAsync<string?>(new CommandDefinition(
            LockRow, new { TransType = transType }, transaction, cancellationToken: cancellationToken));
    }

    public Task SaveLastAsync(string transType, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, new { TransType = transType, LastNo = lastNo, User = user }, transaction, cancellationToken: cancellationToken));
}

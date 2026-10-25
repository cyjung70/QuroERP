using System.Data;
using Dapper;
using QuroErp.Application.Common.Codes;

namespace QuroErp.Infrastructure.Common.Codes;

internal sealed class CodeRepository : ICodeRepository
{
    private const string SelectActiveMajorExists = """
        SELECT EXISTS (SELECT 1 FROM sys_code_major WHERE major_code = @MajorCode AND use_yn)
        """;

    private const string SelectActiveMinors = """
        SELECT n.id, n.minor_code, n.sort_order
        FROM sys_code_minor n
        JOIN sys_code_major m ON m.id = n.major_id
        WHERE m.major_code = @MajorCode AND m.use_yn AND n.use_yn
        ORDER BY n.sort_order, n.minor_code
        """;

    private const string SelectActiveMinorExists = """
        SELECT EXISTS (
            SELECT 1
            FROM sys_code_minor n
            JOIN sys_code_major m ON m.id = n.major_id
            WHERE m.major_code = @MajorCode AND n.minor_code = @MinorCode AND m.use_yn AND n.use_yn)
        """;

    public async Task<IReadOnlyList<CodeMinorRow>?> FindActiveMinorsAsync(
        string majorCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var majorExists = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectActiveMajorExists, new { MajorCode = majorCode }, transaction, cancellationToken: cancellationToken));
        if (!majorExists)
        {
            return null;
        }
        var rows = await connection.QueryAsync<CodeMinorRow>(
            new CommandDefinition(SelectActiveMinors, new { MajorCode = majorCode }, transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public Task<bool> ExistsActiveAsync(string majorCode, string minorCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            SelectActiveMinorExists, new { MajorCode = majorCode, MinorCode = minorCode }, transaction, cancellationToken: cancellationToken));
}

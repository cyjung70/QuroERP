using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using QuroErp.Application.Bc.Codes;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Infrastructure.Bc.Codes;

internal sealed partial class CodeMinorRepository : ICodeMinorRepository
{
    private const string SelectMajor = "SELECT id FROM sys_code_major WHERE major_code = @MajorCode";

    private const string LockMajor = SelectMajor + " FOR UPDATE";

    private const string SelectByMajor = """
        SELECT id, minor_code, sort_order, use_yn, attr1, attr2, updated_at
        FROM sys_code_minor
        WHERE major_id = @MajorId
        ORDER BY sort_order, minor_code
        """;

    private const string Insert = """
        INSERT INTO sys_code_minor (major_id, minor_code, sort_order, use_yn, attr1, attr2, created_by, updated_by)
        VALUES (@MajorId, @MinorCode, @SortOrder, @UseYn, @Attr1, @Attr2, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE sys_code_minor
        SET sort_order = @SortOrder, use_yn = @UseYn, attr1 = @Attr1, attr2 = @Attr2, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteOtherNames = "DELETE FROM sys_code_minor_translation WHERE minor_id = @MinorId AND NOT (lang_code = ANY(@LangCodes))";

    private const string UpsertName = """
        INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
        VALUES (@MinorId, @LangCode, @Name, @By, @By)
        ON CONFLICT (minor_id, lang_code) DO UPDATE
        SET name = EXCLUDED.name, updated_at = now(), updated_by = EXCLUDED.updated_by
        WHERE sys_code_minor_translation.name <> EXCLUDED.name
        """;

    private const string Delete = "DELETE FROM sys_code_minor WHERE id = ANY(@Ids)";

    public Task<long?> FindMajorIdAsync(string majorCode, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            forUpdate ? LockMajor : SelectMajor, new { MajorCode = majorCode }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<CodeMinorRecord>> FindByMajorAsync(long majorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string MinorCode, int SortOrder, bool UseYn, string? Attr1, string? Attr2, DateTime UpdatedAt)>(
            new CommandDefinition(SelectByMajor, new { MajorId = majorId }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new CodeMinorRecord(r.Id, r.MinorCode, r.SortOrder, r.UseYn, r.Attr1, r.Attr2, Utc(r.UpdatedAt)))
        .ToList();

    public async Task<IReadOnlySet<string>> FindUsedAsync(
        string majorCode, IReadOnlyCollection<string> minorCodes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var columns = CodeUsages.Of(majorCode);
        if (minorCodes.Count == 0 || columns.Count == 0)
        {
            return new HashSet<string>();
        }
        return (await transaction.Connection!.QueryAsync<string>(new CommandDefinition(
            UsedSql(columns), new { Codes = minorCodes.ToArray() }, transaction, cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);
    }

    public Task<long> InsertAsync(
        long majorId, string minorCode, CodeMinorFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { MajorId = majorId, MinorCode = minorCode, fields.SortOrder, fields.UseYn, fields.Attr1, fields.Attr2, By = createdBy },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, CodeMinorFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { Id = id, fields.SortOrder, fields.UseYn, fields.Attr1, fields.Attr2, By = updatedBy, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task ReplaceNamesAsync(
        long minorId, IReadOnlyDictionary<string, string> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteOtherNames, new { MinorId = minorId, LangCodes = names.Keys.ToArray() }, transaction, cancellationToken: cancellationToken));
        foreach (var (langCode, name) in names)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                UpsertName, new { MinorId = minorId, LangCode = langCode, Name = name, By = updatedBy }, transaction, cancellationToken: cancellationToken));
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// One SELECT per column that stores the Major's codes. Table and column names come only from the Domain CodeUsages list, never from
    /// a request, and are checked against a plain identifier pattern before they go into the SQL; the codes are a parameter.
    /// </summary>
    private static string UsedSql(IEnumerable<CodeColumn> columns) =>
        string.Join("\nUNION\n", columns.Select(c =>
            $"SELECT \"{Identifier(c.Column)}\" FROM \"{Identifier(c.Table)}\" WHERE \"{Identifier(c.Column)}\" = ANY(@Codes)"));

    private static string Identifier(string name) =>
        IdentifierPattern().IsMatch(name) ? name : throw new InvalidOperationException($"Not a plain identifier: {name}");

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}

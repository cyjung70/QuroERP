using System.Data;
using Dapper;
using QuroErp.Application.Bc.Codes;

namespace QuroErp.Infrastructure.Bc.Codes;

internal sealed class CodeMajorRepository : ICodeMajorRepository
{
    private const string Select = """
        SELECT m.id, m.major_code, m.use_yn, (SELECT count(*)::int FROM sys_code_minor n WHERE n.major_id = m.id) AS minor_count, m.updated_at
        FROM sys_code_major m
        WHERE @Prefix::varchar IS NULL OR starts_with(m.major_code, @Prefix)
        ORDER BY m.major_code
        """;

    private const string Lock = Select + "\nFOR UPDATE OF m";

    private const string Insert = """
        INSERT INTO sys_code_major (major_code, use_yn, created_by, updated_by)
        VALUES (@MajorCode, @UseYn, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE sys_code_major
        SET use_yn = @UseYn, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteNames = "DELETE FROM sys_code_major_translation WHERE major_id = @MajorId AND lang_code = ANY(@LangCodes)";

    private const string UpsertName = """
        INSERT INTO sys_code_major_translation (major_id, lang_code, name, created_by, updated_by)
        VALUES (@MajorId, @LangCode, @Name, @By, @By)
        ON CONFLICT (major_id, lang_code) DO UPDATE
        SET name = EXCLUDED.name, updated_at = now(), updated_by = EXCLUDED.updated_by
        WHERE sys_code_major_translation.name <> EXCLUDED.name
        """;

    private const string Delete = "DELETE FROM sys_code_major WHERE id = ANY(@Ids)";

    public async Task<IReadOnlyList<CodeMajorRecord>> FindAsync(
        string? codePrefix, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string MajorCode, bool UseYn, int MinorCount, DateTime UpdatedAt)>(
            new CommandDefinition(forUpdate ? Lock : Select, new { Prefix = codePrefix }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new CodeMajorRecord(r.Id, r.MajorCode, r.UseYn, r.MinorCount, Utc(r.UpdatedAt)))
        .ToList();

    public Task<long> InsertAsync(string majorCode, bool useYn, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert, new { MajorCode = majorCode, UseYn = useYn, By = createdBy }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, bool useYn, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { Id = id, UseYn = useYn, By = updatedBy, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task SaveNamesAsync(
        long majorId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var removed = names.Where(n => n.Value is null).Select(n => n.Key).ToArray();
        if (removed.Length > 0)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                DeleteNames, new { MajorId = majorId, LangCodes = removed }, transaction, cancellationToken: cancellationToken));
        }
        foreach (var (langCode, name) in names.Where(n => n.Value is not null))
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                UpsertName, new { MajorId = majorId, LangCode = langCode, Name = name, By = updatedBy }, transaction, cancellationToken: cancellationToken));
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

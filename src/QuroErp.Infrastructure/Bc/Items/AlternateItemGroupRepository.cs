using System.Data;
using Dapper;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Infrastructure.Bc.Items;

/// <summary>Alternate item groups on bc_alternate_item_group (BC-UC-019).</summary>
internal sealed class AlternateItemGroupRepository : IAlternateItemGroupRepository
{
    private const string Lock = "SELECT pg_advisory_xact_lock(hashtext('bc_alternate_item_group'))";

    private const string SelectRows = """
        SELECT g.id, g.alt_item_group_cd, g.alt_item_group_name, g.class AS item_class,
               (SELECT count(*) FROM bc_item i WHERE i.alternate_item_group_id = g.id) AS item_count, g.updated_at
        FROM bc_alternate_item_group g
        """;

    // Code order as ordinal text, so the order does not depend on the database collation
    private const string Search = SelectRows + """

        WHERE (@ItemClass::varchar IS NULL OR g.class = @ItemClass)
          AND (@Pattern::varchar IS NULL OR g.alt_item_group_cd ILIKE @Pattern ESCAPE '\' OR g.alt_item_group_name ILIKE @Pattern ESCAPE '\')
        ORDER BY g.alt_item_group_cd COLLATE "C"
        LIMIT @Limit
        """;

    private const string SelectForUpdate = SelectRows + " WHERE g.id = ANY(@Ids) FOR UPDATE OF g";

    private const string SelectCodeOwners = "SELECT id, alt_item_group_cd FROM bc_alternate_item_group WHERE alt_item_group_cd = ANY(@Codes)";

    private const string Delete = "DELETE FROM bc_alternate_item_group WHERE id = ANY(@Ids)";

    // Codes are first moved aside (a trimmed code never starts with a space), so two rows can swap codes without hitting the unique key
    private const string Update = """
        UPDATE bc_alternate_item_group SET alt_item_group_cd = ' #' || id WHERE id = ANY(@Ids);
        UPDATE bc_alternate_item_group g
        SET alt_item_group_cd = r.code, alt_item_group_name = r.name, updated_at = now(), updated_by = @UserId
        FROM unnest(@Ids, @Codes, @Names) AS r (id, code, name)
        WHERE g.id = r.id;
        """;

    private const string Insert = """
        INSERT INTO bc_alternate_item_group (alt_item_group_cd, alt_item_group_name, class, created_by, updated_by)
        SELECT code, name, @ItemClass, @UserId, @UserId FROM unnest(@Codes, @Names) AS r (code, name)
        """;

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, transaction: transaction, cancellationToken: cancellationToken));

    public Task<IReadOnlyList<AlternateItemGroupRow>> SearchAsync(
        string? itemClass, string? keyword, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryAsync(
            Search,
            new { ItemClass = itemClass, Pattern = keyword is null ? null : "%" + EscapeLike(keyword) + "%", Limit = limit },
            transaction,
            cancellationToken);

    public Task<IReadOnlyList<AlternateItemGroupRow>> FindForUpdateAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryAsync(SelectForUpdate, new { Ids = ids.ToArray() }, transaction, cancellationToken);

    public async Task<IReadOnlyList<(long Id, string Code)>> FindCodeOwnersAsync(
        IReadOnlyCollection<string> codes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string Code)>(new CommandDefinition(
            SelectCodeOwners, new { Codes = codes.ToArray() }, transaction, cancellationToken: cancellationToken))).ToList();

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    public Task UpdateAsync(
        IReadOnlyList<(long Id, AlternateItemGroupFields Fields)> rows, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Ids = rows.Select(r => r.Id).ToArray(),
                Codes = rows.Select(r => r.Fields.AltItemGroupCd).ToArray(),
                Names = rows.Select(r => r.Fields.AltItemGroupName).ToArray(),
                UserId = updatedBy,
            },
            transaction,
            cancellationToken: cancellationToken));

    public Task InsertAsync(
        IReadOnlyList<AlternateItemGroupFields> rows, string itemClass, string createdBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert,
            new
            {
                Codes = rows.Select(r => r.AltItemGroupCd).ToArray(),
                Names = rows.Select(r => r.AltItemGroupName).ToArray(),
                ItemClass = itemClass,
                UserId = createdBy,
            },
            transaction,
            cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as a UTC DateTime
    private static async Task<IReadOnlyList<AlternateItemGroupRow>> QueryAsync(
        string sql, object parameters, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string AltItemGroupCd, string AltItemGroupName, string ItemClass, long ItemCount, DateTime UpdatedAt)>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)))
        .Select(r => new AlternateItemGroupRow(
            r.Id, r.AltItemGroupCd, r.AltItemGroupName, r.ItemClass, r.ItemCount, new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}

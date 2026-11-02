using System.Data;
using Dapper;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Infrastructure.Bc.Items;

/// <summary>Alternate item group members on bc_item.alternate_item_group_id (BC-UC-012). Items point to the group by id (MM-BR-814).</summary>
internal sealed class AlternateItemRepository : IAlternateItemRepository
{
    private const string SelectGroup = "SELECT id, alt_item_group_cd, alt_item_group_name, updated_at FROM bc_alternate_item_group";

    // Item code order as ordinal text, so the order does not depend on the database collation
    private const string SelectMembers = """
        SELECT id, item_cd, item_name
        FROM bc_item
        WHERE alternate_item_group_id = @GroupId
        ORDER BY item_cd COLLATE "C"
        """;

    private const string SelectCandidates = """
        SELECT i.id, i.item_cd, i.type AS item_type, i.valid_flag, i.alternate_item_group_id AS group_id, g.alt_item_group_cd AS group_cd
        FROM bc_item i
        LEFT JOIN bc_alternate_item_group g ON g.id = i.alternate_item_group_id
        WHERE i.item_cd = ANY(@ItemCds)
        FOR UPDATE OF i
        """;

    // The groups the joining items leave change too, so a stale screen of that group gets 409 (Common-E-004)
    private const string Save = """
        UPDATE bc_alternate_item_group SET updated_at = now(), updated_by = @UserId
        WHERE id = @GroupId OR id IN (SELECT alternate_item_group_id FROM bc_item WHERE id = ANY(@JoinIds));
        UPDATE bc_item SET alternate_item_group_id = @GroupId, updated_at = now(), updated_by = @UserId
        WHERE id = ANY(@JoinIds);
        UPDATE bc_item SET alternate_item_group_id = NULL, updated_at = now(), updated_by = @UserId
        WHERE id = ANY(@LeaveIds) AND alternate_item_group_id = @GroupId;
        """;

    public Task<AlternateItemGroupRecord?> FindGroupByCodeAsync(string groupCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindGroupAsync(SelectGroup + " WHERE alt_item_group_cd = @Code", new { Code = groupCode }, transaction, cancellationToken);

    public Task<AlternateItemGroupRecord?> FindGroupAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindGroupAsync(SelectGroup + " WHERE id = @Id" + (forUpdate ? " FOR UPDATE" : string.Empty), new { Id = id }, transaction, cancellationToken);

    public async Task<IReadOnlyList<AlternateItemMemberRecord>> FindMembersAsync(
        long groupId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<AlternateItemMemberRecord>(new CommandDefinition(
            SelectMembers, new { GroupId = groupId }, transaction, cancellationToken: cancellationToken))).ToList();

    public async Task<IReadOnlyList<AlternateItemCandidate>> FindCandidatesAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<AlternateItemCandidate>(new CommandDefinition(
            SelectCandidates, new { ItemCds = itemCds.ToArray() }, transaction, cancellationToken: cancellationToken))).ToList();

    public Task SaveAsync(
        long groupId, IReadOnlyCollection<long> joinIds, IReadOnlyCollection<long> leaveIds, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Save,
            new { GroupId = groupId, JoinIds = joinIds.ToArray(), LeaveIds = leaveIds.ToArray(), UserId = updatedBy },
            transaction,
            cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as a UTC DateTime
    private static async Task<AlternateItemGroupRecord?> FindGroupAsync(
        string sql, object parameters, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string AltItemGroupCd, string AltItemGroupName, DateTime UpdatedAt)>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)))
        .Select(r => new AlternateItemGroupRecord(
            r.Id, r.AltItemGroupCd, r.AltItemGroupName, new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .SingleOrDefault();
}

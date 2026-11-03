using System.Data;
using Dapper;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;

namespace QuroErp.Infrastructure.Bc.FieldPolicies;

internal sealed class FieldPolicyRepository : IFieldPolicyRepository
{
    private const string SelectAll = """
        SELECT id, field_key, state, default_value, label_key, updated_at, updated_by
        FROM bc_field_policy
        ORDER BY field_key
        """;

    private const string Upsert = """
        INSERT INTO bc_field_policy (field_key, state, default_value, label_key, created_by, updated_by)
        VALUES (@FieldKey, @State, @DefaultValue, @LabelKey, @By, @By)
        ON CONFLICT (field_key) DO UPDATE
        SET state = EXCLUDED.state, default_value = EXCLUDED.default_value, label_key = EXCLUDED.label_key,
            updated_at = now(), updated_by = EXCLUDED.updated_by
        """;

    private const string Delete = "DELETE FROM bc_field_policy WHERE field_key = ANY(@FieldKeys)";

    public async Task<IReadOnlyList<FieldPolicyOverrideDto>> FindAllAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string FieldKey, string? State, string? DefaultValue, string? LabelKey, DateTime UpdatedAt,
            string UpdatedBy)>(new CommandDefinition(SelectAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => new FieldPolicyOverrideDto(
            r.Id, r.FieldKey, r.State, r.DefaultValue, r.LabelKey, new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc)), r.UpdatedBy))
        .ToList();

    public Task UpsertAsync(string fieldKey, FieldPolicyEntry entry, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Upsert,
            new { FieldKey = fieldKey, entry.State, entry.DefaultValue, entry.LabelKey, By = updatedBy },
            transaction,
            cancellationToken: cancellationToken));

    public Task DeleteAsync(IReadOnlyCollection<string> fieldKeys, IDbTransaction transaction, CancellationToken cancellationToken) =>
        fieldKeys.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(
                Delete, new { FieldKeys = fieldKeys.ToArray() }, transaction, cancellationToken: cancellationToken));
}

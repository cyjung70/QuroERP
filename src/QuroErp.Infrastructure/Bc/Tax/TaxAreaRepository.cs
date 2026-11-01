using System.Data;
using Dapper;
using QuroErp.Application.Bc.Tax;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Infrastructure.Bc.Tax;

internal sealed class TaxAreaRepository : ITaxAreaRepository
{
    // Tax area saves wait for each other until the transaction ends, so two saves cannot take the same code (BC-BR-511)
    private const string Lock = "SELECT pg_advisory_xact_lock(hashtext('bc_tax_area'))";

    private const string Search = """
        SELECT a.id, a.tax_area_code, a.tax_area_name, a.state_code, r.effective_from, r.rate
        FROM bc_tax_area a
        LEFT JOIN LATERAL (
            SELECT effective_from, rate FROM bc_tax_area_rate
            WHERE tax_area_id = a.id AND effective_from <= @Today
            ORDER BY effective_from DESC
            LIMIT 1) r ON true
        WHERE (@Pattern::varchar IS NULL OR a.tax_area_code ILIKE @Pattern ESCAPE '\' OR a.tax_area_name ILIKE @Pattern ESCAPE '\')
          AND (@StateCode::varchar IS NULL OR a.state_code = @StateCode)
        ORDER BY a.tax_area_code
        LIMIT @Limit
        """;

    private const string Select = "SELECT id, tax_area_code, tax_area_name, state_code, updated_at FROM bc_tax_area WHERE id = @Id";

    private const string SelectRates = "SELECT effective_from, rate FROM bc_tax_area_rate WHERE tax_area_id = @Id ORDER BY effective_from";

    private const string SelectIdByCode = "SELECT id FROM bc_tax_area WHERE tax_area_code = @Code";

    private const string UsedByTaxCodes = "SELECT EXISTS (SELECT 1 FROM bc_tax_code WHERE tax_area_id = @Id)";

    private const string Insert = """
        INSERT INTO bc_tax_area (tax_area_code, tax_area_name, state_code, created_by, updated_by)
        VALUES (@TaxAreaCode, @TaxAreaName, @StateCode, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE bc_tax_area
        SET tax_area_code = @TaxAreaCode, tax_area_name = @TaxAreaName, state_code = @StateCode, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteOtherRates = "DELETE FROM bc_tax_area_rate WHERE tax_area_id = @Id AND effective_from <> ALL(@Dates::date[])";

    private const string UpsertRates = """
        INSERT INTO bc_tax_area_rate (tax_area_id, effective_from, rate, created_by, updated_by)
        SELECT @Id, t.effective_from, t.rate, @By, @By FROM unnest(@Dates::date[], @Rates::numeric[]) AS t(effective_from, rate)
        ON CONFLICT (tax_area_id, effective_from) DO UPDATE
        SET rate = EXCLUDED.rate, updated_at = now(), updated_by = EXCLUDED.updated_by
        WHERE bc_tax_area_rate.rate <> EXCLUDED.rate
        """;

    private const string DeleteRates = "DELETE FROM bc_tax_area_rate WHERE tax_area_id = @Id";

    private const string Delete = "DELETE FROM bc_tax_area WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<TaxAreaSummaryRecord>> SearchAsync(
        string? keyword, string? stateCode, DateOnly today, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<TaxAreaSummaryRecord>(new CommandDefinition(
            Search,
            new { Pattern = keyword is null ? null : "%" + EscapeLike(keyword) + "%", StateCode = stateCode, Today = today, Limit = limit },
            transaction,
            cancellationToken: cancellationToken))).AsList();

    public async Task<TaxAreaRecord?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string TaxAreaCode, string TaxAreaName, string StateCode, DateTime UpdatedAt)>(
            new CommandDefinition(forUpdate ? Select + " FOR UPDATE" : Select, new { Id = id }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new TaxAreaRecord(
            r.Id, r.TaxAreaCode, r.TaxAreaName, r.StateCode, new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .SingleOrDefault();

    public async Task<IReadOnlyList<TaxAreaRate>> FindRatesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<TaxAreaRate>(new CommandDefinition(
            SelectRates, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task<long?> FindIdByCodeAsync(string taxAreaCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long?>(new CommandDefinition(
            SelectIdByCode, new { Code = taxAreaCode }, transaction, cancellationToken: cancellationToken));

    public Task<bool> IsUsedByTaxCodesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            UsedByTaxCodes, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public Task<long> InsertAsync(TaxAreaFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { fields.TaxAreaCode, fields.TaxAreaName, fields.StateCode, By = createdBy },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, TaxAreaFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id,
                fields.TaxAreaCode,
                fields.TaxAreaName,
                fields.StateCode,
                By = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task SaveRatesAsync(long id, TaxAreaFields fields, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var dates = fields.Rates.Select(r => r.EffectiveFrom).ToArray();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteOtherRates, new { Id = id, Dates = dates }, transaction, cancellationToken: cancellationToken));
        if (dates.Length > 0)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                UpsertRates,
                new { Id = id, Dates = dates, Rates = fields.Rates.Select(r => r.Rate).ToArray(), By = updatedBy },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(DeleteRates, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime }, transaction, cancellationToken: cancellationToken)) == 1;
    }

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}

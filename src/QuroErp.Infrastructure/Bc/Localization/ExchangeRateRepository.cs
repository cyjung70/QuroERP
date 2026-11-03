using System.Data;
using Dapper;
using QuroErp.Application.Bc.Localization;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Infrastructure.Bc.Localization;

internal sealed class ExchangeRateRepository : IExchangeRateRepository
{
    private const string Columns = "id, rate_dt, currency_code, rate, updated_at";

    // Exchange rate saves wait for each other until the transaction ends (SD-BR-906)
    private const string Lock = "SELECT pg_advisory_xact_lock(hashtext('bc_exchange_rate'))";

    private const string SelectPeriod = $"""
        SELECT {Columns}
        FROM bc_exchange_rate
        WHERE rate_dt BETWEEN @From AND @To AND (@CurrencyCode::varchar IS NULL OR currency_code = @CurrencyCode)
        ORDER BY rate_dt DESC, currency_code
        """;

    private const string SelectByIds = $"SELECT {Columns} FROM bc_exchange_rate WHERE id = ANY(@Ids)";

    private const string SelectByKeys = $"""
        SELECT {Columns}
        FROM bc_exchange_rate
        WHERE (rate_dt, currency_code) IN (SELECT * FROM unnest(@Dates::date[], @Codes::varchar[]))
        """;

    private const string SelectLatest = """
        SELECT rate
        FROM bc_exchange_rate
        WHERE currency_code = @CurrencyCode AND rate_dt <= @OnOrBefore
        ORDER BY rate_dt DESC
        LIMIT 1
        """;

    private const string Insert = """
        INSERT INTO bc_exchange_rate (rate_dt, currency_code, rate, created_by, updated_by)
        VALUES (@RateDt, @CurrencyCode, @Rate, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE bc_exchange_rate
        SET rate_dt = @RateDt, currency_code = @CurrencyCode, rate = @Rate, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM bc_exchange_rate WHERE id = ANY(@Ids)";

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, transaction: transaction, cancellationToken: cancellationToken));

    public Task<IReadOnlyList<ExchangeRateRecord>> FindAsync(
        DateOnly first, DateOnly last, string? currencyCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryAsync(SelectPeriod, new { From = first, To = last, CurrencyCode = currencyCode }, transaction, cancellationToken);

    public Task<IReadOnlyList<ExchangeRateRecord>> FindByIdsAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.FromResult<IReadOnlyList<ExchangeRateRecord>>([])
            : QueryAsync(SelectByIds, new { Ids = ids.ToArray() }, transaction, cancellationToken);

    public Task<IReadOnlyList<ExchangeRateRecord>> FindByKeysAsync(
        IReadOnlyCollection<(DateOnly RateDt, string CurrencyCode)> keys, IDbTransaction transaction, CancellationToken cancellationToken) =>
        keys.Count == 0
            ? Task.FromResult<IReadOnlyList<ExchangeRateRecord>>([])
            : QueryAsync(
                SelectByKeys,
                new { Dates = keys.Select(k => k.RateDt).ToArray(), Codes = keys.Select(k => k.CurrencyCode).ToArray() },
                transaction,
                cancellationToken);

    public Task<decimal?> FindLatestAsync(string currencyCode, DateOnly onOrBefore, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            SelectLatest, new { CurrencyCode = currencyCode, OnOrBefore = onOrBefore }, transaction, cancellationToken: cancellationToken));

    public Task<long> InsertAsync(ExchangeRateFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert, new { fields.RateDt, fields.CurrencyCode, fields.Rate, By = createdBy }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, ExchangeRateFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { Id = id, fields.RateDt, fields.CurrencyCode, fields.Rate, By = updatedBy, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static async Task<IReadOnlyList<ExchangeRateRecord>> QueryAsync(
        string sql, object parameters, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, DateOnly RateDt, string CurrencyCode, decimal Rate, DateTime UpdatedAt)>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)))
        .Select(r => new ExchangeRateRecord(r.Id, r.RateDt, r.CurrencyCode, r.Rate, new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();
}

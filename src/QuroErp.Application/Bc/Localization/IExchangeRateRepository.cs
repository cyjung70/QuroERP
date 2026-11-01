using System.Data;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Bc.Localization;

/// <summary>A stored exchange rate.</summary>
public sealed record ExchangeRateRecord(long Id, DateOnly RateDt, string CurrencyCode, decimal Rate, DateTimeOffset UpdatedAt);

/// <summary>Exchange rates (bc_exchange_rate). Callers pass their own transaction.</summary>
public interface IExchangeRateRepository
{
    /// <summary>Makes exchange rate saves run one at a time until the transaction ends.</summary>
    Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Rates first..last (inclusive), of one currency or all, date newest first, then currency (SD-BR-908).</summary>
    Task<IReadOnlyList<ExchangeRateRecord>> FindAsync(
        DateOnly first, DateOnly last, string? currencyCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The rows with the given ids that still exist.</summary>
    Task<IReadOnlyList<ExchangeRateRecord>> FindByIdsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The rows stored for any of the given dates and currencies.</summary>
    Task<IReadOnlyList<ExchangeRateRecord>> FindByKeysAsync(
        IReadOnlyCollection<(DateOnly RateDt, string CurrencyCode)> keys, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The rate of the latest date on or before the given date, or null (BC-BR-412).</summary>
    Task<decimal?> FindLatestAsync(string currencyCode, DateOnly onOrBefore, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(ExchangeRateFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the row changed after expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, ExchangeRateFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}

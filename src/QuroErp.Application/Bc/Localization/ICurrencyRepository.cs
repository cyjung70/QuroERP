using System.Data;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Bc.Localization;

/// <summary>A stored currency without its names.</summary>
public sealed record CurrencyRecord(long Id, string CurrencyCode, int Decimals, string? CountryCode, bool UseYn, DateTimeOffset UpdatedAt);

/// <summary>Currency master (bc_currency, bc_currency_translation). Callers pass their own transaction.</summary>
public interface ICurrencyRepository
{
    /// <summary>Every currency in currency_code order. forUpdate locks the rows so currency saves run one at a time.</summary>
    Task<IReadOnlyList<CurrencyRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given currency codes that a document stores (CurrencyRules.DocumentColumns).</summary>
    Task<IReadOnlySet<string>> FindUsedByDocumentsAsync(
        IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(string currencyCode, CurrencyFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the row changed after expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, CurrencyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets the given names and removes the null ones. Languages not given keep their names (BC-BR-405).</summary>
    Task SaveNamesAsync(
        long currencyId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the rows; their translations go with them.</summary>
    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}

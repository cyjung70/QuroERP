using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Bc.Localization;

/// <summary>Currency API routes (BC-UC-026).</summary>
public static class CurrencyRoutes
{
    /// <summary>GET ?lang=&amp;useYn= (every currency in code order).</summary>
    public const string Base = "api/bc/currencies";

    /// <summary>POST the grid changes in one transaction (screens.md "유형별 저장 API 형태").</summary>
    public const string Batch = Base + "/batch";
}

/// <summary>A currency (bc_currency) with its names (bc_currency_translation).</summary>
/// <param name="Name">Name in the requested language, falling back to the company default language, then en (BC-BR-402).</param>
/// <param name="Names">Name by language code, for every language that has one. en is always there (BC-BR-404).</param>
/// <param name="Decimals">Amount decimals, 0~4 (BC-BR-411).</param>
/// <param name="CountryCode">Country whose unit price decimals the currency's documents use, or null (BC-BR-428, BC-BR-429).</param>
/// <param name="InUseByDocuments">A document stores the currency: decimals and country cannot change (BC-BR-411, BC-BR-428).</param>
public sealed record CurrencyMasterDto(
    long Id, string CurrencyCode, string? Name, IReadOnlyDictionary<string, string> Names, int Decimals, string? CountryCode, bool UseYn,
    bool InUseByDocuments, DateTimeOffset UpdatedAt);

/// <summary>
/// A new or changed currency row. A new row has no Id. A changed row sends the Id and the updatedAt it read (409 when stale);
/// its currency code cannot change: send the stored code or none.
/// </summary>
/// <param name="Decimals">Null for a new currency means the ISO 4217 value (BC-BR-411).</param>
/// <param name="Names">Name changes by language; en is required, a blank name removes that language (BC-BR-404, BC-BR-405).</param>
/// <param name="UseYn">Null means in use.</param>
public sealed record CurrencySaveRow(
    long? Id, string? CurrencyCode, int? Decimals, string? CountryCode, bool? UseYn, IReadOnlyDictionary<string, string?>? Names,
    DateTimeOffset? UpdatedAt);

/// <summary>A deleted currency in a <see cref="BatchRequest{TRow, TKey}"/>: its Id and the updatedAt it read.</summary>
public sealed record CurrencyKey(long Id, DateTimeOffset UpdatedAt);

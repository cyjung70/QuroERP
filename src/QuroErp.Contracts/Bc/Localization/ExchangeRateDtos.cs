namespace QuroErp.Contracts.Bc.Localization;

/// <summary>Exchange rate API routes (BC-UC-024).</summary>
public static class ExchangeRateRoutes
{
    /// <summary>GET ?from=&amp;to=&amp;currencyCode= (date newest first, then currency).</summary>
    public const string Base = "api/bc/exchange-rates";

    /// <summary>POST the grid changes in one transaction (screens.md "유형별 저장 API 형태").</summary>
    public const string Batch = Base + "/batch";
}

/// <summary>An exchange rate (bc_exchange_rate).</summary>
/// <param name="Rate">The base currency amount per 1 unit of the currency, above 0, up to 10 decimals (SD-BR-907).</param>
public sealed record ExchangeRateDto(long Id, DateOnly RateDt, string CurrencyCode, decimal Rate, DateTimeOffset UpdatedAt);

/// <summary>
/// A new or changed exchange rate row. A new row has no Id. A changed row sends the Id and the updatedAt it read (409 when stale);
/// its date and currency may change (SD-BR-906).
/// </summary>
public sealed record ExchangeRateSaveRow(long? Id, DateOnly? RateDt, string? CurrencyCode, decimal? Rate, DateTimeOffset? UpdatedAt);

/// <summary>A deleted exchange rate in a batch: its Id and the updatedAt it read.</summary>
public sealed record ExchangeRateKey(long Id, DateTimeOffset UpdatedAt);

using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>An exchange rate after normalizing: the base currency amount per 1 unit of the currency on that date (SD-BR-907).</summary>
public sealed record ExchangeRateFields(DateOnly RateDt, string CurrencyCode, decimal Rate);

/// <summary>
/// Exchange rate rules (BC-UC-024, SD-BR-906, SD-BR-907, SD-BR-908, SD-BR-909, BC-BR-412). Every error names the row's date
/// (yyyy-MM-dd, empty when missing) and currency code.
/// </summary>
public static class ExchangeRateRules
{
    /// <summary>A second rate for the same date and currency (SD-BR-906). Args: date, currency code.</summary>
    public const string DuplicateCode = "SD-E-906";

    /// <summary>
    /// The date is missing, the currency is not one in use or is the base currency, or the rate is missing, not above 0 or has
    /// more than 10 decimals (SD-BR-907). Args: date, currency code.
    /// </summary>
    public const string InvalidCode = "SD-E-907";

    /// <summary>
    /// SD-BR-907 for one row: a date, a currency in use that is not the base currency (trimmed, upper case) and a rate above 0
    /// with at most 10 decimals. Trailing zeros do not count as decimals.
    /// </summary>
    /// <exception cref="DomainException">SD-E-907 with the date and currency code.</exception>
    public static ExchangeRateFields Normalize(
        DateOnly? rateDt, string? currencyCode, decimal? rate, IReadOnlySet<string> currenciesInUse, string? baseCurrencyCode)
    {
        var code = currencyCode?.Trim().ToUpperInvariant() ?? "";
        if (rateDt is not { } date
            || !currenciesInUse.Contains(code)
            || string.Equals(code, baseCurrencyCode, StringComparison.Ordinal)
            || rate is not { } value
            || value <= 0
            || Rounding.DecimalPlaces(value) > ExchangeRate.Decimals)
        {
            throw new DomainException(InvalidCode, Format(rateDt), code);
        }
        return new ExchangeRateFields(date, code, value);
    }

    /// <summary>SD-BR-906: the rates the save leaves have one row per date and currency.</summary>
    /// <exception cref="DomainException">SD-E-906 with the first repeated date and currency code.</exception>
    public static void EnsureUnique(IEnumerable<(DateOnly RateDt, string CurrencyCode)> keys)
    {
        var seen = new HashSet<(DateOnly, string)>();
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw new DomainException(DuplicateCode, Format(key.RateDt), key.CurrencyCode);
            }
        }
    }

    /// <summary>SD-BR-908: the list period. A missing end is today, a missing start one year before the end.</summary>
    public static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var end = to ?? today;
        return (from ?? end.AddYears(-1), end);
    }

    /// <summary>
    /// SD-BR-909: a legacy rate (units of the currency per 1 USD) as a rate (USD per 1 unit), 1 / value rounded to 10 decimals.
    /// Null when the legacy value is missing or not above 0; that date has no rate for the currency.
    /// </summary>
    public static decimal? FromLegacy(decimal? unitsPerUsd) =>
        unitsPerUsd is > 0 and var value ? Math.Round(1m / value, ExchangeRate.Decimals, MidpointRounding.AwayFromZero) : null;

    /// <summary>
    /// BC-BR-412: the default rate of a document in the currency on the date. The base currency is 1; otherwise the latest rate on
    /// or before the date (stored is that rate, or null when there is none and the user must enter one).
    /// </summary>
    public static decimal? DefaultRate(string currencyCode, string? baseCurrencyCode, decimal? stored) =>
        string.Equals(currencyCode, baseCurrencyCode, StringComparison.Ordinal) ? ExchangeRate.BaseCurrencyRate : stored;

    private static string Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

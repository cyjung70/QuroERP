using System.Globalization;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Localization;

/// <summary>Exchange rate API client (api/bc/exchange-rates, BC-UC-024), for the rate a document takes.</summary>
public sealed class ExchangeRateApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The rate of a currency on a date (base currency per 1 unit, BC-BR-412), or null when none is entered.</summary>
    public async Task<decimal?> RateOnAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var uri = $"{ExchangeRateRoutes.Base}?from={day}&to={day}&currencyCode={Uri.EscapeDataString(currencyCode)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var rates = await response.Content.ReadFromJsonAsync<List<ExchangeRateDto>>(cancellationToken) ?? [];
        return rates.FirstOrDefault(r => r.CurrencyCode == currencyCode && r.RateDt == date)?.Rate;
    }
}

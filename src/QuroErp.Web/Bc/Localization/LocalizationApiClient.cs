using QuroErp.Contracts.Bc.Localization;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Localization;

/// <summary>Localization API client (GET api/bc/localization): input decimals and display formats (BC-BR-426).</summary>
public sealed class LocalizationApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<LocalizationDto> GetAsync(string lang, CancellationToken cancellationToken)
    {
        var uri = $"{LocalizationRoutes.Base}?lang={Uri.EscapeDataString(lang)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<LocalizationDto>(cancellationToken)
            ?? throw new InvalidOperationException("Localization API returned an empty body.");
    }
}

/// <summary>Decimals the screens read from <see cref="LocalizationDto"/>.</summary>
public static class LocalizationDecimals
{
    /// <summary>Unit price decimals of the company country for a module (BC-BR-422), null when not set.</summary>
    public static int? UnitPrice(this LocalizationDto localization, string module) =>
        localization.CountryDecimals.FirstOrDefault(d => d.CountryCode == localization.CountryCode && d.Module == module)?.UnitPriceDecimals;

    /// <summary>Quantity decimals of a module, null when not set.</summary>
    public static int? Qty(this LocalizationDto localization, string module) =>
        localization.DecimalSettings.FirstOrDefault(d => d.Module == module)?.QtyDecimals;

    /// <summary>Amount decimals of the base currency, null when the currency is not listed.</summary>
    public static int? BaseAmount(this LocalizationDto localization) =>
        localization.Currencies.FirstOrDefault(c => c.CurrencyCode == localization.BaseCurrencyCode)?.Decimals;
}

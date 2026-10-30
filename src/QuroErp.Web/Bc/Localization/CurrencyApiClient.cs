using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Localization;

/// <summary>Currency API client (api/bc/currencies, BC-UC-026).</summary>
public sealed class CurrencyApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Every currency in code order, in use or not, with names in lang.</summary>
    public async Task<IReadOnlyList<CurrencyMasterDto>> SearchAsync(string lang, CancellationToken cancellationToken)
    {
        var uri = $"{CurrencyRoutes.Base}?lang={Uri.EscapeDataString(lang)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">
    /// 422 BC-E-411, BC-E-428, BC-E-404 or BC-E-312 naming the row's currency, BC-E-405; 409 Common-E-004, or BC-E-411 and BC-E-428
    /// when documents use the currency.
    /// </exception>
    public async Task<IReadOnlyList<CurrencyMasterDto>> SaveAsync(
        BatchRequest<CurrencySaveRow, CurrencyKey> request, string lang, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{CurrencyRoutes.Batch}?lang={Uri.EscapeDataString(lang)}")
        {
            Content = JsonContent.Create(request),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<CurrencyMasterDto>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<CurrencyMasterDto>>(cancellationToken)
        ?? throw new InvalidOperationException("Currency API returned an empty body.");
}

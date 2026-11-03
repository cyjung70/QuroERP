using QuroErp.Contracts.Bc.Tax;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Tax;

/// <summary>Tax code API client (api/bc/tax-codes, BC-UC-028), read only: the choice lists of other screens.</summary>
public sealed class TaxCodeApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The tax codes in use, in code order, with names in <paramref name="lang"/>.</summary>
    public async Task<IReadOnlyList<TaxCodeDto>> InUseAsync(string lang, CancellationToken cancellationToken)
    {
        var uri = $"{TaxCodeRoutes.Base}?lang={Uri.EscapeDataString(lang)}&useYn=true";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<TaxCodeDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Tax code API returned an empty body.");
    }
}

using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Companies;

/// <summary>Business place API client (api/bc/business-places, BC-BR-319). The company screen edits the default place.</summary>
public sealed class BusinessPlaceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, BusinessPlaceRoutes.Base), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<BusinessPlaceDto>>(cancellationToken) ?? [];
    }

    /// <exception cref="ApiProblemException">422 BC-E-320 naming the property or BC-E-319, 404, 409 Common-E-004, 403 for non-admin users.</exception>
    public async Task<BusinessPlaceDto> UpdateAsync(long id, UpdateBusinessPlaceRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, BusinessPlaceRoutes.For(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BusinessPlaceDto>(cancellationToken)
            ?? throw new InvalidOperationException("Business place API returned an empty body.");
    }
}

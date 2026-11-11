using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.SetItems;

/// <summary>Set item composition API client (api/bc/set-items, BC-UC-013).</summary>
public sealed class SetItemApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The set and its components in sequence order.</summary>
    /// <exception cref="ApiProblemException">422 Common-E-005 without a code, SD-E-501 when the item is not a set in use.</exception>
    public async Task<SetItemDto> GetAsync(string itemCd, CancellationToken cancellationToken)
    {
        var uri = $"{SetItemRoutes.Base}?itemCd={Uri.EscapeDataString(itemCd)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<SetItemDto>(cancellationToken)
            ?? throw new InvalidOperationException("Set item API returned an empty body.");
    }

    /// <summary>Saves the changed rows in one transaction (SD-BR-506) and returns the set as saved.</summary>
    /// <exception cref="ApiProblemException">
    /// 422 SD-E-501~504, MM-E-361 or BC-E-423 with the row's item code; 409 Common-E-004; 403 for users without Full Access.
    /// </exception>
    public async Task<SetItemDto> SaveAsync(
        string itemCd, BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{SetItemRoutes.Base}/{SetItemRoutes.Batch}?itemCd={Uri.EscapeDataString(itemCd)}")
        {
            Content = JsonContent.Create(request),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SetItemDto>(cancellationToken)
            ?? throw new InvalidOperationException("Set item API returned an empty body.");
    }
}

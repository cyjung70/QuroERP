using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.SalesPrices;

/// <summary>Sales price API client (api/bc/sales-prices, BC-UC-014).</summary>
public sealed class SalesPriceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The price level's rows in item code order, with the item standard sales price (SD-BR-409).</summary>
    /// <exception cref="ApiProblemException">422 Common-E-005 without a level, SD-E-403 for a level that is not 5 upper case characters.</exception>
    public async Task<IReadOnlyList<SalesPriceDto>> SearchAsync(string priceLevel, CancellationToken cancellationToken)
    {
        var uri = $"{SalesPriceRoutes.Base}?priceLevel={Uri.EscapeDataString(priceLevel)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Saves the changed rows in one transaction (SD-BR-409) and returns the level's rows as saved.</summary>
    /// <exception cref="ApiProblemException">
    /// 422 SD-E-403, SD-E-407, SD-E-408 or BC-E-423 with the row's item code; 409 Common-E-004; 403 for users without Full Access.
    /// </exception>
    public async Task<IReadOnlyList<SalesPriceDto>> SaveAsync(
        string priceLevel, BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> request, CancellationToken cancellationToken)
    {
        var uri = $"{SalesPriceRoutes.Base}/{SalesPriceRoutes.Batch}?priceLevel={Uri.EscapeDataString(priceLevel)}";
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<SalesPriceDto>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<SalesPriceDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Sales price API returned an empty body.");
}

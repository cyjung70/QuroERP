using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.PurchasePrices;

/// <summary>Purchase price API client (api/bc/purchase-prices, BC-UC-015), read by documents that price items for a vendor.</summary>
public sealed class PurchasePriceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The prices of a vendor price group by item id; empty when the group has none.</summary>
    public async Task<IReadOnlyDictionary<long, decimal>> GroupAsync(string vendorPrice, CancellationToken cancellationToken)
    {
        var uri = $"{PurchasePriceRoutes.Base}?vendorPrice={Uri.EscapeDataString(vendorPrice)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var rows = await response.Content.ReadFromJsonAsync<List<PurchasePriceDto>>(cancellationToken) ?? [];
        return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.First().PurchasePrc);
    }
}

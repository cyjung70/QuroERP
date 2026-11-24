using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.StockTransfers;

/// <summary>Stock transfer API client (api/mm/stock-transfers) for the PDA move (MM-UC-039).</summary>
public sealed class StockTransferApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Reads a place scanned at a step: FROM takes a Location or Pallet label, TO a Location label (MM-BR-923).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-923, Common-E-002, Common-E-005.</exception>
    public async Task<StockTransferPlaceDto> ScanPlaceAsync(string value, string step, CancellationToken cancellationToken)
    {
        var uri = $"{StockTransferRoutes.ScanPlace}?value={Uri.EscapeDataString(value)}&step={Uri.EscapeDataString(step)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<StockTransferPlaceDto>(cancellationToken)
            ?? throw new InvalidOperationException("The stock transfer API returned no place.");
    }

    /// <summary>Saves a transfer; MovType null is LT inside the sending warehouse and TR to another (MM-BR-922).</summary>
    /// <exception cref="ApiProblemException">422 with the transfer's checks (MM-E-001, MM-E-715, MM-E-912, …); 403 without write access.</exception>
    public async Task<StockTransferDto> CreateAsync(SaveStockTransferRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, StockTransferRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<StockTransferDto>(cancellationToken)
            ?? throw new InvalidOperationException("The stock transfer API returned no transfer.");
    }
}

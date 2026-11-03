using QuroErp.Contracts.Bc.Items;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Items;

/// <summary>Item barcode API client (api/bc/item-barcodes, BC-UC-011).</summary>
public sealed class ItemBarcodeApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <exception cref="ApiProblemException">422 MM-E-802 without an item class, Common-E-002 for an unknown code value.</exception>
    public async Task<IReadOnlyList<ItemBarcodeDto>> ListAsync(string itemClass, string? validFlag, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, ItemBarcodeRoutes.Base + Query(itemClass, validFlag, null)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ItemBarcodeDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Item barcode API returned an empty body.");
    }

    /// <summary>What a run would do; nothing is saved (MM-BR-815~817).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-802, MM-E-816, Common-E-002.</exception>
    public async Task<ItemBarcodePreviewDto> PreviewAsync(
        string itemClass, string? validFlag, string? startNo, CancellationToken cancellationToken)
    {
        var uri = $"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}{Query(itemClass, validFlag, startNo)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<ItemBarcodePreviewDto>(cancellationToken)
            ?? throw new InvalidOperationException("Item barcode API returned an empty body.");
    }

    /// <exception cref="ApiProblemException">422 MM-E-802, MM-E-816, MM-E-817; 403 for users without Full Access.</exception>
    public async Task<GenerateItemBarcodesResultDto> GenerateAsync(GenerateItemBarcodesRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Generate}")
        {
            Content = JsonContent.Create(request),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GenerateItemBarcodesResultDto>(cancellationToken)
            ?? throw new InvalidOperationException("Item barcode API returned an empty body.");
    }

    private static string Query(string itemClass, string? validFlag, string? startNo) =>
        $"?itemClass={Uri.EscapeDataString(itemClass)}"
        + (string.IsNullOrWhiteSpace(validFlag) ? "" : $"&validFlag={Uri.EscapeDataString(validFlag)}")
        + (string.IsNullOrWhiteSpace(startNo) ? "" : $"&startNo={Uri.EscapeDataString(startNo.Trim())}");
}

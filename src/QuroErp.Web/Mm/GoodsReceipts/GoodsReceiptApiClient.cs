using System.Globalization;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.GoodsReceipts;

/// <summary>Goods receipt API client (api/mm/goods-receipts, MM-UC-004).</summary>
public sealed class GoodsReceiptApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Receipts that match the conditions, newest first (at most <see cref="GoodsReceiptRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<GoodsReceiptSummaryDto>> SearchAsync(GoodsReceiptSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "grNo", query.GrNo?.Trim());
        Add(parameters, "vendorId", query.VendorId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "fromDt", query.FromDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parameters, "toDt", query.ToDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var uri = parameters.Count == 0 ? GoodsReceiptRoutes.Base : $"{GoodsReceiptRoutes.Base}?{string.Join('&', parameters)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<GoodsReceiptSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The receipt with exactly this number, or null.</summary>
    public async Task<GoodsReceiptDto?> FindByNoAsync(string grNo, CancellationToken cancellationToken)
    {
        var number = grNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new GoodsReceiptSearchQuery(number, null, null, null), cancellationToken))
            .FirstOrDefault(r => r.GrNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the receipt is gone.</exception>
    public async Task<GoodsReceiptDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, ReceiptUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Lines of the vendor's approved and partly received orders with a quantity left to receive (MM-BR-113).</summary>
    public async Task<IReadOnlyList<OpenPoLineDto>> OpenPoLinesAsync(long vendorId, CancellationToken cancellationToken)
    {
        var uri = $"{GoodsReceiptRoutes.Base}/{GoodsReceiptRoutes.OpenPoLines}?vendorId={vendorId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<OpenPoLineDto>>(cancellationToken) ?? [];
    }

    /// <exception cref="ApiProblemException">422 MM-E-1nn, or MM-W-102 (confirmField confirmOverReceipt) in WARN mode.</exception>
    public async Task<GoodsReceiptDto> CreateAsync(SaveGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, GoodsReceiptRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, MM-E-104 when matched or returned; 409 Common-E-004.</exception>
    public async Task<GoodsReceiptDto> UpdateAsync(long id, SaveGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, ReceiptUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-104, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{ReceiptUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string ReceiptUri(long id) => $"{GoodsReceiptRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<GoodsReceiptDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<GoodsReceiptDto>(cancellationToken)
            ?? throw new InvalidOperationException("Goods receipt API returned an empty body.");
}

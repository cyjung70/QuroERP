using System.Globalization;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.PurchaseOrders;

/// <summary>A file the API sent, e.g. the purchase order form as PDF.</summary>
public sealed record DownloadedFile(byte[] Content, string FileName);

/// <summary>Purchase order API client (api/mm/purchase-orders, MM-UC-003).</summary>
public sealed class PurchaseOrderApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Orders that match the conditions, newest first (at most <see cref="PurchaseOrderRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<PurchaseOrderSummaryDto>> SearchAsync(PurchaseOrderSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "poNo", query.PoNo?.Trim());
        Add(parameters, "vendorId", query.VendorId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "status", query.Status);
        Add(parameters, "fromDt", query.FromDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parameters, "toDt", query.ToDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var uri = parameters.Count == 0 ? PurchaseOrderRoutes.Base : $"{PurchaseOrderRoutes.Base}?{string.Join('&', parameters)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<PurchaseOrderSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The order with exactly this number, or null.</summary>
    public async Task<PurchaseOrderDto?> FindByNoAsync(string poNo, CancellationToken cancellationToken)
    {
        var number = poNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new PurchaseOrderSearchQuery(number, null, null, null, null), cancellationToken))
            .FirstOrDefault(o => o.PoNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the order is gone.</exception>
    public async Task<PurchaseOrderDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, OrderUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-0nn or MM-W-016 (confirmField updateVendorPrice).</exception>
    public async Task<PurchaseOrderDto> CreateAsync(SavePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, PurchaseOrderRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, MM-E-013 after a receipt; 409 Common-E-004 when someone saved first.</exception>
    public async Task<PurchaseOrderDto> UpdateAsync(long id, SavePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, OrderUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-013 after a receipt, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{OrderUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    /// <summary>DR → PO (MM-BR-020).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-019 or MM-E-020, 409 Common-E-004.</exception>
    public Task<PurchaseOrderDto> ApproveAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        ActionAsync(id, PurchaseOrderRoutes.Approve, updatedAt, cancellationToken);

    /// <summary>PO or RE → MC (MM-BR-022).</summary>
    public Task<PurchaseOrderDto> CloseAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        ActionAsync(id, PurchaseOrderRoutes.Close, updatedAt, cancellationToken);

    /// <summary>PO with nothing received → CN (MM-BR-023).</summary>
    public Task<PurchaseOrderDto> CancelAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        ActionAsync(id, PurchaseOrderRoutes.Cancel, updatedAt, cancellationToken);

    /// <summary>The purchase order form as PDF in the user's language (print-documents.md).</summary>
    public Task<DownloadedFile> PdfAsync(long id, string poNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{OrderUri(id)}/{PurchaseOrderRoutes.PrintPdf}", poNo + ".pdf", cancellationToken);

    /// <summary>The same content as one XLSX sheet.</summary>
    public Task<DownloadedFile> XlsxAsync(long id, string poNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{OrderUri(id)}/{PurchaseOrderRoutes.PrintXlsx}", poNo + ".xlsx", cancellationToken);

    private async Task<PurchaseOrderDto> ActionAsync(long id, string action, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{OrderUri(id)}/{action}")
        {
            Content = JsonContent.Create(new PurchaseOrderActionRequest(updatedAt)),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private async Task<DownloadedFile> DownloadAsync(string uri, string fileName, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), string.IsNullOrEmpty(name) ? fileName : name);
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string OrderUri(long id) => $"{PurchaseOrderRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<PurchaseOrderDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<PurchaseOrderDto>(cancellationToken)
            ?? throw new InvalidOperationException("Purchase order API returned an empty body.");
}

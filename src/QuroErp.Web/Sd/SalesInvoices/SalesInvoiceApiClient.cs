using System.Globalization;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Mm.PurchaseOrders;

namespace QuroErp.Web.Sd.SalesInvoices;

/// <summary>Sales invoice API client (api/sd/sales-invoices, SD-UC-003) and the unbilled shipment lines it imports (SD-BR-115).</summary>
public sealed class SalesInvoiceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Invoices that match the conditions, newest first (at most <see cref="SalesInvoiceRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<SalesInvoiceSummaryDto>> SearchAsync(SalesInvoiceSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "dnNo", query.DnNo?.Trim());
        Add(parameters, "customerId", query.CustomerId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "status", query.Status);
        Add(parameters, "fromDt", Date(query.FromDt));
        Add(parameters, "toDt", Date(query.ToDt));
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, Uri(SalesInvoiceRoutes.Base, parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SalesInvoiceSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The invoice with exactly this number, or null.</summary>
    public async Task<SalesInvoiceDto?> FindByNoAsync(string dnNo, CancellationToken cancellationToken)
    {
        var number = dnNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new SalesInvoiceSearchQuery(number, null, null, null, null), cancellationToken))
            .FirstOrDefault(s => s.DnNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the invoice is gone or a Credit Memo.</exception>
    public async Task<SalesInvoiceDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, InvoiceUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-1nn, the inventory checks, or SD-W-009 / SD-W-309 with a confirmField.</exception>
    public async Task<SalesInvoiceDto> CreateAsync(SaveSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, SalesInvoiceRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, SD-E-104, 107, 119, 136, 144; 409 Common-E-004.</exception>
    public async Task<SalesInvoiceDto> UpdateAsync(long id, SaveSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, InvoiceUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-104, 113, 144; 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{InvoiceUri(id)}?updatedAt={System.Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    /// <summary>Voids the invoice (SD-BR-113).</summary>
    /// <exception cref="ApiProblemException">422 SD-E-104, 113, 144; 409 Common-E-004.</exception>
    public async Task<SalesInvoiceDto> VoidAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{InvoiceUri(id)}/{SalesInvoiceRoutes.Void}")
        {
            Content = JsonContent.Create(new VoidSalesInvoiceRequest(updatedAt)),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>BILL_THEN_SHIP order lines of the customer in this currency left to bill (SD-BR-141).</summary>
    public async Task<IReadOnlyList<OpenBillFirstOrderLineDto>> OpenOrderLinesAsync(long customerId, string currencyCode, CancellationToken cancellationToken)
    {
        var uri = $"{SalesInvoiceRoutes.Base}/{SalesInvoiceRoutes.OpenOrderLines}?customerId={customerId.ToString(CultureInfo.InvariantCulture)}"
                  + $"&currencyCode={System.Uri.EscapeDataString(currencyCode)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<OpenBillFirstOrderLineDto>>(cancellationToken) ?? [];
    }

    /// <summary>SHIP_THEN_BILL shipment lines of the customer in this currency left to bill (SD-BR-115).</summary>
    public async Task<IReadOnlyList<UnbilledShipmentLineDto>> UnbilledShipmentLinesAsync(
        long customerId, string currencyCode, CancellationToken cancellationToken)
    {
        var uri = $"{ShipmentRoutes.Base}/{ShipmentRoutes.UnbilledLines}?customerId={customerId.ToString(CultureInfo.InvariantCulture)}"
                  + $"&currencyCode={System.Uri.EscapeDataString(currencyCode)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<UnbilledShipmentLineDto>>(cancellationToken) ?? [];
    }

    /// <summary>Cancels unshipped qty and makes a Credit Memo (SD-BR-145).</summary>
    /// <exception cref="ApiProblemException">422 SD-E-145; 409 Common-E-004.</exception>
    public async Task<CancelUnshippedResult> CancelUnshippedAsync(long id, CancelUnshippedRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{InvoiceUri(id)}/{SalesInvoiceRoutes.CancelUnshipped}")
        {
            Content = JsonContent.Create(request),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CancelUnshippedResult>(cancellationToken)
               ?? throw new InvalidOperationException("Sales invoice API returned an empty body.");
    }

    /// <summary>Undoes a cancellation and deletes its Credit Memo (SD-BR-146).</summary>
    /// <exception cref="ApiProblemException">422 SD-E-146; 409 Common-E-004.</exception>
    public async Task UndoCancellationAsync(long id, long creditMemoId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{InvoiceUri(id)}/{SalesInvoiceRoutes.Cancellations}/{creditMemoId.ToString(CultureInfo.InvariantCulture)}"
                  + $"?updatedAt={System.Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    /// <summary>The invoice as PDF in the user's language.</summary>
    public Task<DownloadedFile> InvoicePdfAsync(long id, string dnNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{InvoiceUri(id)}/{SalesInvoiceRoutes.PrintInvoice}", dnNo, cancellationToken);

    /// <summary>The KR tax invoice as PDF (SD-BR-134).</summary>
    /// <exception cref="ApiProblemException">422 SD-E-134.</exception>
    public Task<DownloadedFile> TaxInvoicePdfAsync(long id, string dnNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{InvoiceUri(id)}/{SalesInvoiceRoutes.PrintTaxInvoice}", dnNo + "-tax", cancellationToken);

    private async Task<DownloadedFile> DownloadAsync(string uri, string name, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var file = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), string.IsNullOrEmpty(file) ? name + ".pdf" : file);
    }

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={System.Uri.EscapeDataString(value)}");
        }
    }

    private static string Uri(string path, List<string> parameters) =>
        parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";

    private static string InvoiceUri(long id) => $"{SalesInvoiceRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<SalesInvoiceDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<SalesInvoiceDto>(cancellationToken)
            ?? throw new InvalidOperationException("Sales invoice API returned an empty body.");
}

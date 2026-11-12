using System.Globalization;
using System.Net.Http.Headers;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Mm.PurchaseOrders;

namespace QuroErp.Web.Sd.SalesOrders;

/// <summary>Sales order API client (api/sd/sales-orders, SD-UC-002).</summary>
public sealed class SalesOrderApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Orders that match the conditions, newest first (at most <see cref="SalesOrderRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<SalesOrderSummaryDto>> SearchAsync(SalesOrderSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "soNo", query.SoNo?.Trim());
        Add(parameters, "customerId", query.CustomerId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "customerName", query.CustomerName?.Trim());
        Add(parameters, "status", query.Status);
        Add(parameters, "fromDt", Date(query.FromDt));
        Add(parameters, "toDt", Date(query.ToDt));
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, Uri(SalesOrderRoutes.Base, parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SalesOrderSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The order with exactly this number, or null.</summary>
    public async Task<SalesOrderDto?> FindByNoAsync(string soNo, CancellationToken cancellationToken)
    {
        var number = soNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new SalesOrderSearchQuery(number, null, null, null, null), cancellationToken))
            .FirstOrDefault(o => o.SoNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the order is gone.</exception>
    public async Task<SalesOrderDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, OrderUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-0nn, or SD-W-009, SD-W-010 or SD-W-309 with a confirmField.</exception>
    public async Task<SalesOrderDto> CreateAsync(SaveSalesOrderRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, SalesOrderRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, SD-E-003 when not SO; 409 Common-E-004 or SD-E-035.</exception>
    public async Task<SalesOrderDto> UpdateAsync(long id, SaveSalesOrderRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, OrderUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-003, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{OrderUri(id)}?updatedAt={System.Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    /// <summary>The values of a new line for the item (SD-BR-016, 033, 401, 405).</summary>
    /// <exception cref="ApiProblemException">404 Common-E-006 for an unknown item, 422 MM-E-809 for an item not in use.</exception>
    public async Task<SalesOrderLineDefaultsDto> LineDefaultsAsync(SalesOrderLineDefaultsQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "customerId", query.CustomerId.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "itemCd", query.ItemCd.Trim());
        Add(parameters, "shipState", query.ShipState);
        Add(parameters, "soDt", Date(query.SoDt));
        var uri = Uri($"{SalesOrderRoutes.Base}/{SalesOrderRoutes.LineDefaults}", parameters);
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<SalesOrderLineDefaultsDto>(cancellationToken)
            ?? throw new InvalidOperationException("Sales order API returned an empty body.");
    }

    /// <summary>Lines of a line or barcode file, or its error rows (SD-BR-017~022). Nothing is saved.</summary>
    /// <exception cref="ApiProblemException">422 SD-E-017 for a file over 1 MB or 1,000 lines.</exception>
    public async Task<SalesOrderLineFileResult> LineFileAsync(
        SalesOrderLineFileQuery query, Stream file, string fileName, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "customerId", query.CustomerId.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "kind", query.Kind);
        Add(parameters, "shipState", query.ShipState);
        Add(parameters, "soDt", Date(query.SoDt));
        using var content = new MultipartFormDataContent();
        var part = new StreamContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(part, SalesOrderRoutes.LineFileFormField, fileName);
        using var message = new HttpRequestMessage(HttpMethod.Post, Uri($"{SalesOrderRoutes.Base}/{SalesOrderRoutes.LineFile}", parameters))
        {
            Content = content,
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SalesOrderLineFileResult>(cancellationToken)
            ?? throw new InvalidOperationException("Sales order API returned an empty body.");
    }

    /// <summary>The discount codes a line can take, with their rates (SD-BR-404).</summary>
    public async Task<IReadOnlyList<SalesOrderDiscountRateDto>> DiscountRatesAsync(CancellationToken cancellationToken)
    {
        var uri = $"{SalesOrderRoutes.Base}/{SalesOrderRoutes.DiscountRates}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SalesOrderDiscountRateDto>>(cancellationToken) ?? [];
    }

    /// <summary>The sales order form as PDF in the user's language (print-documents.md).</summary>
    public Task<DownloadedFile> SalesOrderPdfAsync(long id, string soNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{OrderUri(id)}/{SalesOrderRoutes.PrintSalesOrder}", soNo + ".pdf", cancellationToken);

    /// <summary>The picking list as PDF.</summary>
    public Task<DownloadedFile> PickingListPdfAsync(long id, string soNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{OrderUri(id)}/{SalesOrderRoutes.PrintPickingList}", soNo + "-picking.pdf", cancellationToken);

    /// <summary>The set component list as PDF.</summary>
    public Task<DownloadedFile> SetComponentListPdfAsync(long id, string soNo, CancellationToken cancellationToken) =>
        DownloadAsync($"{OrderUri(id)}/{SalesOrderRoutes.PrintSetComponentList}", soNo + "-set.pdf", cancellationToken);

    private async Task<DownloadedFile> DownloadAsync(string uri, string fileName, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), string.IsNullOrEmpty(name) ? fileName : name);
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

    private static string OrderUri(long id) => $"{SalesOrderRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<SalesOrderDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<SalesOrderDto>(cancellationToken)
            ?? throw new InvalidOperationException("Sales order API returned an empty body.");
}

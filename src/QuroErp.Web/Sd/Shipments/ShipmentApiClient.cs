using System.Globalization;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Mm.PurchaseOrders;

namespace QuroErp.Web.Sd.Shipments;

/// <summary>Shipment API client (api/sd/shipments, SD-UC-051).</summary>
public sealed class ShipmentApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Shipments that match the conditions, newest first (at most <see cref="ShipmentRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<ShipmentSummaryDto>> SearchAsync(ShipmentSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "shipNo", query.ShipNo?.Trim());
        Add(parameters, "customerId", query.CustomerId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "customerName", query.CustomerName?.Trim());
        Add(parameters, "status", query.Status);
        Add(parameters, "fromDt", Date(query.FromDt));
        Add(parameters, "toDt", Date(query.ToDt));
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, Uri(ShipmentRoutes.Base, parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ShipmentSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The shipment with exactly this number, or null.</summary>
    public async Task<ShipmentDto?> FindByNoAsync(string shipNo, CancellationToken cancellationToken)
    {
        var number = shipNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new ShipmentSearchQuery(number, null, null, null, null), cancellationToken))
            .FirstOrDefault(s => s.ShipNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the shipment is gone.</exception>
    public async Task<ShipmentDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, ShipmentUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-1nn, the inventory checks, or SD-W-309 with a confirmField.</exception>
    public async Task<ShipmentDto> CreateAsync(SaveShipmentRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ShipmentRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, SD-E-104, SD-E-107, SD-E-168; 409 Common-E-004.</exception>
    public async Task<ShipmentDto> UpdateAsync(long id, SaveShipmentRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, ShipmentUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Voids the shipment (SD-BR-169).</summary>
    /// <exception cref="ApiProblemException">422 SD-E-104, SD-E-168; 409 Common-E-004.</exception>
    public async Task<ShipmentDto> VoidAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ShipmentUri(id)}/{ShipmentRoutes.Void}")
        {
            Content = JsonContent.Create(new VoidShipmentRequest(updatedAt)),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>The customer's orders with lines left to ship (SD-BR-172).</summary>
    public async Task<IReadOnlyList<OpenSalesOrderDto>> OpenOrderLinesAsync(long customerId, CancellationToken cancellationToken)
    {
        var uri = $"{ShipmentRoutes.Base}/{ShipmentRoutes.OpenOrderLines}?customerId={customerId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<OpenSalesOrderDto>>(cancellationToken) ?? [];
    }

    /// <summary>The customer's BILL_THEN_SHIP invoice lines left to ship (SD-BR-175).</summary>
    public async Task<IReadOnlyList<UnshippedInvoiceLineDto>> UnshippedInvoiceLinesAsync(long customerId, CancellationToken cancellationToken)
    {
        var uri = $"{ShipmentRoutes.Base}/{ShipmentRoutes.UnshippedInvoiceLines}?customerId={customerId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<UnshippedInvoiceLineDto>>(cancellationToken) ?? [];
    }

    /// <summary>The delivery note as PDF in the user's language.</summary>
    public async Task<DownloadedFile> DeliveryNotePdfAsync(long id, string shipNo, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, $"{ShipmentUri(id)}/{ShipmentRoutes.Print}"), cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), string.IsNullOrEmpty(name) ? shipNo + ".pdf" : name);
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

    private static string ShipmentUri(long id) => $"{ShipmentRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<ShipmentDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ShipmentDto>(cancellationToken)
            ?? throw new InvalidOperationException("Shipment API returned an empty body.");
}

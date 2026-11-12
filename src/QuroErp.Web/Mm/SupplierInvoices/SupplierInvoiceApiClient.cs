using System.Globalization;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.SupplierInvoices;

/// <summary>Supplier invoice API client (api/mm/supplier-invoices, MM-UC-032).</summary>
public sealed class SupplierInvoiceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Invoices that match the conditions, newest first (at most <see cref="SupplierInvoiceRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<SupplierInvoiceSummaryDto>> SearchAsync(SupplierInvoiceSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "invoiceNo", query.InvoiceNo?.Trim());
        Add(parameters, "vendorId", query.VendorId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "fromDt", query.FromDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parameters, "toDt", query.ToDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parameters, "type", query.Type);
        Add(parameters, "status", query.Status);
        var uri = parameters.Count == 0 ? SupplierInvoiceRoutes.Base : $"{SupplierInvoiceRoutes.Base}?{string.Join('&', parameters)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SupplierInvoiceSummaryDto>>(cancellationToken) ?? [];
    }

    /// <summary>The invoice with exactly this number, or null.</summary>
    public async Task<SupplierInvoiceDto?> FindByNoAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        var number = invoiceNo.Trim().ToUpperInvariant();
        var found = (await SearchAsync(new SupplierInvoiceSearchQuery(number, null, null, null), cancellationToken))
            .FirstOrDefault(i => i.InvoiceNo == number);
        return found is null ? null : await GetAsync(found.Id, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the invoice is gone.</exception>
    public async Task<SupplierInvoiceDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, InvoiceUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>
    /// The vendor's receipt lines with a quantity not yet invoiced, in the currency when given (MM-BR-763). The lines of
    /// <paramref name="excludeInvoiceId"/> count as not invoiced, so a line deleted on screen can be imported again.
    /// </summary>
    public async Task<IReadOnlyList<OpenReceiptLineDto>> OpenReceiptLinesAsync(
        long vendorId, string? currencyCode, long? excludeInvoiceId, CancellationToken cancellationToken)
    {
        var parameters = new List<string> { $"vendorId={vendorId.ToString(CultureInfo.InvariantCulture)}" };
        Add(parameters, "currencyCode", currencyCode);
        Add(parameters, "excludeInvoiceId", excludeInvoiceId?.ToString(CultureInfo.InvariantCulture));
        var uri = $"{SupplierInvoiceRoutes.Base}/{SupplierInvoiceRoutes.OpenReceiptLines}?{string.Join('&', parameters)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<OpenReceiptLineDto>>(cancellationToken) ?? [];
    }

    /// <exception cref="ApiProblemException">422 MM-E-751, 753, 754, 764, or MM-W-754 (confirmField confirmPriceDifference).</exception>
    public async Task<SupplierInvoiceDto> CreateAsync(SaveSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, SupplierInvoiceRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, MM-E-760 and 762; 409 Common-E-004.</exception>
    public async Task<SupplierInvoiceDto> UpdateAsync(long id, SaveSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, InvoiceUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-760, 762; 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{InvoiceUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string InvoiceUri(long id) => $"{SupplierInvoiceRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<SupplierInvoiceDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<SupplierInvoiceDto>(cancellationToken)
            ?? throw new InvalidOperationException("Supplier invoice API returned an empty body.");
}

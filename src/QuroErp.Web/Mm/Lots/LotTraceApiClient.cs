using System.Globalization;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Mm.PurchaseOrders;

namespace QuroErp.Web.Mm.Lots;

/// <summary>Lot trace API client (api/mm/lot-trace, MM-UC-040).</summary>
public sealed class LotTraceApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <exception cref="ApiProblemException">422 Common-E-005.</exception>
    public Task<IReadOnlyList<LotTraceRowDto>> ForwardAsync(LotTraceForwardQuery query, CancellationToken cancellationToken) =>
        RowsAsync(LotTraceRoutes.Forward, ForwardParameters(query), cancellationToken);

    /// <exception cref="ApiProblemException">422 Common-E-005, Common-E-002.</exception>
    public Task<IReadOnlyList<LotTraceRowDto>> BackwardAsync(LotTraceBackwardQuery query, CancellationToken cancellationToken) =>
        RowsAsync(LotTraceRoutes.Backward, BackwardParameters(query), cancellationToken);

    /// <summary>The forward rows as PDF or XLSX (MM-BR-369).</summary>
    public Task<DownloadedFile> ForwardFileAsync(LotTraceForwardQuery query, bool pdf, CancellationToken cancellationToken) =>
        FileAsync(LotTraceRoutes.Forward, ForwardParameters(query), pdf, cancellationToken);

    /// <summary>The backward rows as PDF or XLSX (MM-BR-369).</summary>
    public Task<DownloadedFile> BackwardFileAsync(LotTraceBackwardQuery query, bool pdf, CancellationToken cancellationToken) =>
        FileAsync(LotTraceRoutes.Backward, BackwardParameters(query), pdf, cancellationToken);

    private async Task<IReadOnlyList<LotTraceRowDto>> RowsAsync(string direction, List<string> parameters, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, Uri($"{LotTraceRoutes.Base}/{direction}", parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<LotTraceRowDto>>(cancellationToken) ?? [];
    }

    private async Task<DownloadedFile> FileAsync(string direction, List<string> parameters, bool pdf, CancellationToken cancellationToken)
    {
        var file = pdf ? LotTraceRoutes.ExportPdf : LotTraceRoutes.ExportXlsx;
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, Uri($"{LotTraceRoutes.Base}/{direction}/{file}", parameters)), cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            string.IsNullOrEmpty(name) ? $"lot-trace-{direction}{(pdf ? ".pdf" : ".xlsx")}" : name);
    }

    private static List<string> ForwardParameters(LotTraceForwardQuery query)
    {
        var parameters = new List<string>();
        Add(parameters, "itemId", query.ItemId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "lotNo", query.LotNo);
        return parameters;
    }

    private static List<string> BackwardParameters(LotTraceBackwardQuery query)
    {
        var parameters = new List<string>();
        Add(parameters, "docNo", query.DocNo);
        Add(parameters, "shipmentLineId", query.ShipmentLineId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "invoiceLineId", query.InvoiceLineId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "customerId", query.CustomerId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "from", query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parameters, "to", query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return parameters;
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={System.Uri.EscapeDataString(value)}");
        }
    }

    private static string Uri(string path, List<string> parameters) =>
        parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";
}

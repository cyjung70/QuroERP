using System.Globalization;
using System.Text;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Mm.PurchaseOrders;

namespace QuroErp.Web.Mm.Labels;

/// <summary>Barcode label print API client (api/mm/label-prints: the targets and a print, MM-UC-037).</summary>
public sealed class LabelPrintApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public const string ZplExtension = ".zpl";
    public const string PdfExtension = ".pdf";

    /// <summary>What can be printed under the conditions.</summary>
    /// <exception cref="ApiProblemException">422 Common-E-005 (a Location list without a warehouse) or Common-E-002.</exception>
    public async Task<IReadOnlyList<LabelTargetDto>> TargetsAsync(LabelTargetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conditions = new List<string>();
        Add(conditions, "labelType", query.LabelType);
        Add(conditions, "goodsReceiptId", query.GoodsReceiptId?.ToString(CultureInfo.InvariantCulture));
        Add(conditions, "itemId", query.ItemId?.ToString(CultureInfo.InvariantCulture));
        Add(conditions, "warehouseId", query.WarehouseId?.ToString(CultureInfo.InvariantCulture));
        var uri = $"{LabelPrintRoutes.Targets}?{string.Join('&', conditions)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<LabelTargetDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Label print API returned an empty body.");
    }

    /// <summary>
    /// Prints the targets and returns the file to save: the ZPL text as <c>.zpl</c> (a browser cannot reach the Zebra printer)
    /// or the PDF. The API keeps one history row per target (MM-BR-919).
    /// </summary>
    /// <exception cref="ApiProblemException">422 Common-E-005 or Common-E-002; 404 Common-E-006; 403 without Full Access.</exception>
    public async Task<DownloadedFile> PrintAsync(LabelPrintRequest request, string fileName, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, LabelPrintRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var zpl = await response.Content.ReadFromJsonAsync<LabelPrintZplDto>(cancellationToken)
                ?? throw new InvalidOperationException("Label print API returned an empty body.");
            return new DownloadedFile(Encoding.UTF8.GetBytes(zpl.Zpl), fileName + ZplExtension);
        }
        return new DownloadedFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), fileName + PdfExtension);
    }

    private static void Add(List<string> conditions, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            conditions.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}

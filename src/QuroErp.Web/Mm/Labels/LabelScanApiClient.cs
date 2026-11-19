using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Labels;

/// <summary>Scan API client (api/mm/label-scans, MM-BR-918, MM-BR-923) for the PDA screens.</summary>
public sealed class LabelScanApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Reads a scanned value; accept lists the kinds the current step takes (none: every kind).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-923.</exception>
    public async Task<LabelScanDto> ReadAsync(string value, IReadOnlyCollection<string> accept, CancellationToken cancellationToken)
    {
        var query = string.Concat($"?value={Uri.EscapeDataString(value)}", string.Concat(accept.Select(k => $"&accept={Uri.EscapeDataString(k)}")));
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, LabelScanRoutes.Base + query), cancellationToken);
        return await response.Content.ReadFromJsonAsync<LabelScanDto>(cancellationToken)
            ?? throw new InvalidOperationException("The scan API returned no body.");
    }
}

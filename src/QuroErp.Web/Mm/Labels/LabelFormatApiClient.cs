using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Labels;

/// <summary>Label format API client (api/mm/label-formats: the four formats, batch save and the sample PDF, MM-UC-036).</summary>
public sealed class LabelFormatApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The four formats: PALLET, ITEM, LOT, LOCATION.</summary>
    public async Task<IReadOnlyList<LabelFormatDto>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, LabelFormatRoutes.Base), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<LabelFormatDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Label format API returned an empty body.");
    }

    /// <summary>Saves the changed formats together (MM-BR-917).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-917; 409 Common-E-004.</exception>
    public async Task SaveAsync(BatchRequest<LabelFormatSaveRow, LabelFormatKey> request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, LabelFormatRoutes.Batch) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
    }

    /// <summary>A sample label of the type as a PDF in the saved format (MM-BR-918).</summary>
    /// <exception cref="ApiProblemException">404 Common-E-006.</exception>
    public async Task<byte[]> PreviewAsync(string labelType, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, LabelFormatRoutes.PreviewOf(labelType)), cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

using QuroErp.Contracts.Bc.Codes;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Codes;

/// <summary>Minor code management API client (api/bc/codes, BC-UC-008).</summary>
public sealed class CodeMinorApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Every Minor code of the Major in sort order, with names in lang.</summary>
    /// <exception cref="ApiProblemException">400 Common-E-005 without a Major, 404 Common-E-003 for an unknown one.</exception>
    public async Task<IReadOnlyList<CodeMinorDto>> SearchAsync(string? majorCode, string lang, CancellationToken cancellationToken)
    {
        var uri = $"{CodeMinorRoutes.Base}?majorCode={Uri.EscapeDataString(majorCode?.Trim() ?? "")}&lang={Uri.EscapeDataString(lang)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-911~914, SD-E-916 naming the row's Minor code or BC-E-405, 409 Common-E-004.</exception>
    public async Task<IReadOnlyList<CodeMinorDto>> SaveAsync(SaveCodeMinorsRequest request, string lang, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"{CodeMinorRoutes.Base}?lang={Uri.EscapeDataString(lang)}")
        {
            Content = JsonContent.Create(request),
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<CodeMinorDto>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<CodeMinorDto>>(cancellationToken)
        ?? throw new InvalidOperationException("Code API returned an empty body.");
}

/// <summary>Major code management API client (api/sys/code-majors, BC-UC-023).</summary>
public sealed class CodeMajorApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Majors whose code starts with majorCode (all when blank), in code order, with names in lang.</summary>
    public async Task<IReadOnlyList<CodeMajorDto>> SearchAsync(string? majorCode, string lang, CancellationToken cancellationToken)
    {
        var uri = $"{CodeMajorRoutes.Base}?lang={Uri.EscapeDataString(lang)}";
        if (!string.IsNullOrWhiteSpace(majorCode))
        {
            uri += $"&majorCode={Uri.EscapeDataString(majorCode.Trim())}";
        }
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<CodeMajorDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Code API returned an empty body.");
    }
}

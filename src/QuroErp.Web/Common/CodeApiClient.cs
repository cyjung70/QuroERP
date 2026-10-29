using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Common;

/// <summary>Code API client (GET api/codes/{majorCode}?lang=) for combo boxes and radio buttons (codes.md "화면").</summary>
public sealed class CodeApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <returns>Active codes in sort order with names in <paramref name="lang"/>.</returns>
    public async Task<IReadOnlyList<CodeItemDto>> GetAsync(string majorCode, string lang, CancellationToken cancellationToken)
    {
        var uri = $"{CodeRoutes.Base}/{Uri.EscapeDataString(majorCode)}?lang={Uri.EscapeDataString(lang)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<CodeItemDto>>(cancellationToken) ?? [];
    }
}

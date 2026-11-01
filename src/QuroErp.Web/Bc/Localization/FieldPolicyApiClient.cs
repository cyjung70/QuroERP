using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Localization;

/// <summary>Field policy API client (api/bc/field-policies, field-policy.md 3).</summary>
public sealed class FieldPolicyApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>The screen's merged policies (system default, company country pack, company override).</summary>
    /// <exception cref="ApiProblemException">404 Common-E-006 for a screen that does not take field policies.</exception>
    public async Task<IReadOnlyList<FieldPolicyDto>> GetAsync(string screenCode, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, FieldPolicyRoutes.ForScreen(screenCode)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<FieldPolicyDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Field policy API returned an empty body.");
    }
}

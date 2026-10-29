using System.Net;
using System.Net.Http.Headers;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Companies;

/// <summary>Logo bytes with their MIME type.</summary>
public sealed record CompanyLogoImage(byte[] Content, string ContentType)
{
    /// <summary>For an img src. The logo is at most 1 MB.</summary>
    public string ToDataUrl() => $"data:{ContentType};base64,{Convert.ToBase64String(Content)}";
}

/// <summary>Company API client (api/bc/company and its logo, BC-UC-007).</summary>
public sealed class CompanyApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <exception cref="ApiProblemException">404 BC-E-006 when there is no company.</exception>
    public async Task<CompanyDto> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, CompanyRoutes.Base), cancellationToken);
        return await ReadCompanyAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">
    /// 400 BC-E-313, 422 BC-E-302~316 (field codes name the property), 409 Common-E-004 or BC-E-312/313/316, 403 for non-admin users.
    /// </exception>
    public async Task<CompanyDto> UpdateAsync(UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, CompanyRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadCompanyAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 BC-E-006, 403 for non-admin users.</exception>
    public async Task DeleteLogoAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, CompanyRoutes.Logo), cancellationToken);
    }

    /// <returns>null when no logo has been uploaded.</returns>
    public async Task<CompanyLogoImage?> GetLogoAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAuthorizedAsync(
                tokens, new HttpRequestMessage(HttpMethod.Get, CompanyRoutes.Logo), cancellationToken);
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new CompanyLogoImage(content, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }
        catch (ApiProblemException e) when (e.Problem.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <exception cref="ApiProblemException">422 BC-E-004/BC-E-005, 403 for non-admin users.</exception>
    public async Task UploadLogoAsync(byte[] image, string fileName, CancellationToken cancellationToken)
    {
        var file = new ByteArrayContent(image);
        // The API decides the type from the content; the declared type is informational only
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var message = new HttpRequestMessage(HttpMethod.Put, CompanyRoutes.Logo)
        {
            Content = new MultipartFormDataContent { { file, CompanyRoutes.LogoFormField, fileName } },
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
    }

    private static async Task<CompanyDto> ReadCompanyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<CompanyDto>(cancellationToken)
        ?? throw new InvalidOperationException("Company API returned an empty body.");
}

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

/// <summary>Company API client (GET, PUT api/bc/company/logo).</summary>
public sealed class CompanyApiClient(HttpClient http, AccessTokenProvider tokens)
{
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
}

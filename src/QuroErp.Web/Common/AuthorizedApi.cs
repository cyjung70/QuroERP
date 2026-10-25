using System.Net;
using System.Net.Http.Headers;
using QuroErp.Contracts.Common;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Common;

/// <summary>Sends API requests with the signed-in user's token (Authorization: Bearer).</summary>
public static class AuthorizedApi
{
    /// <returns>The successful response. The caller disposes it.</returns>
    /// <exception cref="ApiProblemException">The API failed, or the session has no token (401).</exception>
    public static async Task<HttpResponseMessage> SendAuthorizedAsync(
        this HttpClient http, AccessTokenProvider tokens, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken)
            ?? throw new ApiProblemException(new ApiProblem((int)HttpStatusCode.Unauthorized, null, []));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }
        using (response)
        {
            throw new ApiProblemException(await ApiProblemReader.ReadAsync(response, cancellationToken));
        }
    }
}

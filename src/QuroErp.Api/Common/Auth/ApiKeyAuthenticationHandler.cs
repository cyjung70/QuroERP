using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>외부 서버의 X-Api-Key 헤더를 확인한다. 키 원문은 로그에 쓰지 않는다 (security.md "로그").</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthRoutes.ApiKeyHeader, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var client = await authenticator.AuthenticateAsync(values.ToString(), Context.RequestAborted);
        if (client is null)
        {
            return AuthenticateResult.Fail("API 키가 올바르지 않거나 폐기되었다.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(AuthClaims.Name, client.ClientCode), new Claim(AuthClaims.Role, client.RoleCode)],
            SchemeName, AuthClaims.Name, AuthClaims.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

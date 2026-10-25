using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>인증·권한 구성 (security.md).</summary>
public static class AuthenticationSetup
{
    /// <summary>X-Api-Key 헤더가 있으면 ApiKey, 없으면 JwtBearer로 인증한다.</summary>
    public const string SelectorScheme = "QuroErp";

    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddQuroAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumKeyBytes,
                $"{JwtOptions.SectionName}:SigningKey가 없거나 {JwtOptions.MinimumKeyBytes}바이트보다 짧다. user-secrets나 환경변수 Jwt__SigningKey로 설정한다.")
            .ValidateOnStart();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, SelectorScheme, o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(AuthRoutes.ApiKeyHeader)
                    ? ApiKeyAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        // 서명 키는 검증된 JwtOptions에서 읽는다
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtAccessTokenIssuer.SigningKey(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AuthClaims.Name,
                    RoleClaimType = AuthClaims.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        // 모든 엔드포인트는 인증이 필요하다. 예외는 로그인 API의 [AllowAnonymous]뿐이다 (AGENTS.md §5)
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // [추정] 로그인 시도 제한: IP마다 1분에 10번
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }
}

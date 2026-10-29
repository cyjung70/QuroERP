using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>인증·권한 구성 (security.md).</summary>
public static class AuthenticationSetup
{
    /// <summary>X-Api-Key 헤더가 있으면 ApiKey, 없으면 JwtBearer로 인증한다.</summary>
    public const string SelectorScheme = "QuroErp";

    /// <summary>Rate limit of every /api/auth/* endpoint.</summary>
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>Signed-in user (JWT) without the password-change gate: me, logout, refresh.</summary>
    public const string SessionPolicy = "session";

    /// <summary>Signed-in user (JWT) with the password-change gate. API clients have no user, so they get 401.</summary>
    public const string UserPolicy = "user";

    /// <summary>Requests per minute per IP on /api/auth/* (login.md "설정", appsettings).</summary>
    public const string RateLimitPerMinuteKey = "Auth:RateLimit:PerMinute";

    public const int DefaultRateLimitPerMinute = 10;

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
            .AddJwtBearer(o => o.Events = new JwtBearerEvents
            {
                // No or expired token: 401 AUTH-006 (login.md "세션")
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    await AuthProblems.WriteAsync(context.HttpContext, AuthCodes.SessionExpired);
                },
            })
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

        // Every endpoint needs authentication (AGENTS.md §5). Exceptions: [AllowAnonymous] login and auto-login, which run without a session
        // A user who must change the password gets 403 AUTH-005 everywhere except the session policy
        var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PasswordChangedRequirement()).Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(signedIn)
            .SetFallbackPolicy(signedIn)
            .AddPolicy(SessionPolicy, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser())
            .AddPolicy(UserPolicy, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser()
                .AddRequirements(new PasswordChangedRequirement()));
        services.AddSingleton<IAuthorizationHandler, PasswordChangedRequirement>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();

        // IP마다 1분에 Auth:RateLimit:PerMinute번 (login.md). Over the limit: 429 AUTH-008
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await AuthProblems.WriteAsync(context.HttpContext, AuthCodes.TooManyRequests);
            };
            o.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                        .GetValue(RateLimitPerMinuteKey, DefaultRateLimitPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }
}

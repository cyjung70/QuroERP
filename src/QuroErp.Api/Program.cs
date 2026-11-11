using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using QuroErp.Api.Common;
using QuroErp.Api.Common.Auth;
using QuroErp.Api.Common.Errors;
using QuroErp.Api.Common.OpenApi;
using QuroErp.Application;
using QuroErp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddHostedService<BusinessClockWarmUp>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQuroAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// Web 서버가 보낸 X-Forwarded-For로 사용자 IP를 남긴다. 기본은 loopback 프록시만 믿는다 [확인 필요: 배포 때 KnownProxies]
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor);

var app = builder.Build();

app.UseForwardedHeaders();

// DomainException → 422, NotFoundException → 404 with ProblemDetails code/args
app.UseExceptionHandler();
// Development only. /openapi/v1.json keeps the sign-in every endpoint needs. Swagger UI (/swagger) and the copy of the
// document it reads open without sign-in for local API checks; the API calls still need the token from POST /api/auth/login
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapOpenApi(SwaggerDocument).AllowAnonymous();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "QuroERP API"));
}

app.UseHttpsRedirection();

// 401·403 등 본문 없는 오류를 ProblemDetails로 돌려준다 (api.md "오류")
app.UseStatusCodePages();

// Request language (Accept-Language) for translated names: ko (default), en (i18n.md)
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture("ko").AddSupportedCultures("ko", "en").AddSupportedUICultures("ko", "en");
    o.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Container health (reports.md "배포"): database and Chromium, no sign-in
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>통합 테스트(WebApplicationFactory)에서 쓴다.</summary>
public partial class Program
{
    /// <summary>Route of the OpenAPI document Swagger UI reads, without sign-in (Development only).</summary>
    public const string SwaggerDocument = "/swagger/{documentName}/swagger.json";
}

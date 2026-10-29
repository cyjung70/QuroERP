using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Mm.Warehouses;

namespace QuroErp.Web.Tests.Mm;

/// <summary>WarehouseApiClient sends the signed-in user's token to the options route (TASK-CORE-022).</summary>
public class WarehouseApiClientTests : BunitContext
{
    private readonly RecordingHandler _api = new();

    public WarehouseApiClientTests()
    {
        var auth = AddAuthorization();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<WarehouseApiClient>(http => http.BaseAddress = new Uri("http://api.test")).ConfigurePrimaryHttpMessageHandler(() => _api);

        var sessionId = Services.GetRequiredService<AccessTokenStore>()
            .Add(new LoginResponse("token-kim", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko"));
        auth.SetAuthorized("kim");
        auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
    }

    [Fact, Trait("Id", "CORE-022-WT-01")]
    public async Task GetOptions_CallsTheOptionsRouteWithTheToken()
    {
        var options = await Services.GetRequiredService<WarehouseApiClient>().GetOptionsAsync(CancellationToken.None);

        options.Should().Equal(new WarehouseOptionDto(1, "MAIN", "Main"));
        _api.Request!.RequestUri!.AbsolutePath.Should().Be("/" + WarehouseRoutes.Options);
        _api.Request.Headers.Authorization!.Parameter.Should().Be("token-kim");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new WarehouseOptionDto(1, "MAIN", "Main") }),
            });
        }
    }
}

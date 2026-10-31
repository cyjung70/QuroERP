using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;

namespace QuroErp.Web.Tests.Bc;

/// <summary>GET /bc/item-image/{itemCd} on the Web host (BC-UC-010, TASK-BC-020): the img src of the item image screen.</summary>
public sealed class ItemImageEndpointTests : IClassFixture<WebFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly WebFactory _factory;
    private readonly List<(string Path, string? Bearer)> _imageRequests = [];

    public ItemImageEndpointTests(WebFactory factory)
    {
        _factory = factory;
        _factory.LoginOptions = null;
        _factory.AuthRouteResponse = _ => null;
        _factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("access", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko")),
        };
        _factory.BusinessApiResponse = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/image", StringComparison.Ordinal))
            {
                lock (_imageRequests)
                {
                    _imageRequests.Add((path, request.Headers.Authorization?.Parameter));
                }
            }
            if (path != "/api/bc/items/A%20100/image")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"status":404,"code":"Common-E-006","args":["B200"]}""", null, "application/problem+json"),
                };
            }
            var content = new ByteArrayContent(Jpeg);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
    }

    [Fact, Trait("Id", "MM-UT-820")]
    public async Task SignedIn_ReturnsTheImageFromTheApi_WithTheUsersToken()
    {
        var browser = await SignInAsync();

        using var response = await browser.GetAsync(ItemImageEndpoint.Of("A 100", 1));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(Jpeg);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        _imageRequests.Should().ContainSingle().Which.Should().Be(("/api/bc/items/A%20100/image", "access"));
    }

    [Fact, Trait("Id", "MM-UT-820")]
    public async Task NoImage_Returns404WithoutAPage()
    {
        var browser = await SignInAsync();

        using var response = await browser.GetAsync(ItemImageEndpoint.Of("B200", 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task SignedOut_DoesNotAskTheApi()
    {
        using var response = await Browser().GetAsync(ItemImageEndpoint.Of("A 100", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be(WebAuthentication.LoginPath);
        _imageRequests.Should().BeEmpty();
    }

    // The session cookie is Secure, so the client uses https to send it back
    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    private async Task<HttpClient> SignInAsync()
    {
        var browser = Browser();
        var form = new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath)),
        };
        using var response = await browser.PostAsync(WebAuthentication.LoginPath, new FormUrlEncodedContent(form));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }
}

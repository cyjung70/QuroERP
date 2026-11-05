using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace QuroErp.Api.Tests.Common.OpenApi;

/// <summary>
/// Swagger UI for local API checks: in Development the page and the copy of the OpenAPI document it reads open without sign-in and
/// the document has the bearer scheme; /openapi/v1.json and the API calls still need a token. Other environments have neither.
/// </summary>
public class SwaggerUiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Development_ServesTheUiAndTheDocument_WithoutSignIn()
    {
        var client = factory.CreateClient();

        var page = await client.GetAsync("/swagger/index.html");
        var document = await client.GetAsync("/swagger/v1/swagger.json");

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        document.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        var scheme = json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        (scheme.GetProperty("type").GetString(), scheme.GetProperty("scheme").GetString()).Should().Be(("http", "bearer"));
        json.RootElement.GetProperty("security")[0].TryGetProperty("Bearer", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Development_TheProtectedDocumentAndApiCalls_StillNeedSignIn()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/mm/goods-receipts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Production_HasNeitherTheUiNorTheDocument()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var page = await client.GetAsync("/swagger/index.html");
        var document = await client.GetAsync("/swagger/v1/swagger.json");

        page.StatusCode.Should().NotBe(HttpStatusCode.OK);
        document.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Common.Codes;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;

namespace QuroErp.Api.Tests.Common.Codes;

/// <summary>Code API and code validation errors (TASK-CORE-005, codes.md, api.md "오류").</summary>
public class CodeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "CORE-005-IT-10")]
    public async Task GetCodes_LangEn_ReturnsTranslatedNamesInSortOrder()
    {
        var client = await SignedInClientAsync();

        var codes = await client.GetFromJsonAsync<List<CodeItemDto>>($"{CodeRoutes.Base}/VALID_FLAG?lang=en");

        codes.Should().Equal(new CodeItemDto("Y", "Used"), new CodeItemDto("N", "Not Used"), new CodeItemDto("S", "S"));
    }

    [Theory, Trait("Id", "CORE-005-IT-11")]
    [InlineData(null, "사용")]
    [InlineData("en-US", "Used")]
    [InlineData("ja", "사용")]
    public async Task GetCodes_NoLangQuery_UsesAcceptLanguageThenCompanyDefault(string? acceptLanguage, string expectedFirstName)
    {
        var client = await SignedInClientAsync();
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        var codes = await client.GetFromJsonAsync<List<CodeItemDto>>($"{CodeRoutes.Base}/VALID_FLAG");

        codes![0].Name.Should().Be(expectedFirstName);
    }

    [Fact, Trait("Id", "CORE-005-IT-12")]
    public async Task GetCodes_NoToken_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{CodeRoutes.Base}/VALID_FLAG");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "CORE-005-IT-13")]
    public async Task GetCodes_UnknownMajor_Returns404WithCode()
    {
        var client = await SignedInClientAsync();

        using var response = await client.GetAsync($"{CodeRoutes.Base}/NO_SUCH_MAJOR");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(CodeMessages.UnknownMajor);
        problem.GetProperty("args").EnumerateArray().Select(a => a.GetString()).Should().Equal("NO_SUCH_MAJOR");
    }

    [Fact, Trait("Id", "CORE-005-IT-14")]
    public async Task SaveWithUnknownCode_Returns422WithCodeAndArgs()
    {
        var client = await SignedInClientAsync();

        using var response = await client.PostAsJsonAsync(CodeValidationProbeController.Path, new ProbeRequest("X"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(CodeMessages.InvalidCode);
        problem.GetProperty("args").EnumerateArray().Select(a => a.GetString()).Should().Equal("VALID_FLAG", "X");
    }

    [Fact, Trait("Id", "CORE-005-IT-15")]
    public async Task SaveWithValidCode_Returns200()
    {
        var client = await SignedInClientAsync();

        using var response = await client.PostAsJsonAsync(CodeValidationProbeController.Path, new ProbeRequest("Y"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Login is rate limited (10 per minute per IP), so the tests share one token
    private static string? _accessToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        if (_accessToken is null)
        {
            using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(ApiFactory.UserId, ApiFactory.Password));
            response.EnsureSuccessStatusCode();
            _accessToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}

public sealed record ProbeRequest(string ValidFlag);

/// <summary>
/// Test-only endpoint that saves a code value the way business services will (quro-api step 3.1).
/// There is no business save API yet, so this stands in for acceptance "없는 코드 저장 시 422".
/// </summary>
[ApiController, Route(Path)]
public sealed class CodeValidationProbeController(ICodeValidator codes) : ControllerBase
{
    public const string Path = "api/test/code-validation";

    [HttpPost]
    public async Task<IActionResult> Save(ProbeRequest request, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync("VALID_FLAG", request.ValidFlag, cancellationToken);
        return Ok();
    }
}

using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Common;

/// <summary>ProblemDetails code로 메시지 표시 (TASK-CORE-004, api.md "오류", i18n.md).</summary>
public class ProblemMessageTests(WebFactory factory) : IClassFixture<WebFactory>
{
    [Fact, Trait("Id", "CORE-004-WT-07")]
    public async Task ReadAsync_ProblemDetails_ReadsCodeAndArgs()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"status":422,"code":"SD-E-001","args":["A-100",3]}""", null, "application/problem+json"),
        };

        var problem = await ApiProblemReader.ReadAsync(response, CancellationToken.None);

        problem.Should().BeEquivalentTo(new ApiProblem(422, "SD-E-001", ["A-100", "3"]));
    }

    [Theory, Trait("Id", "CORE-004-WT-08")]
    [InlineData("text/html", "<html></html>")]
    [InlineData("application/json", "{not json")]
    [InlineData("application/json", "[1]")]
    public async Task ReadAsync_NotProblemDetails_ReturnsStatusWithoutCode(string mediaType, string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(body, null, mediaType),
        };

        var problem = await ApiProblemReader.ReadAsync(response, CancellationToken.None);

        problem.Should().BeEquivalentTo(new ApiProblem(500, null, []));
    }

    [Theory, Trait("Id", "CORE-004-WT-09")]
    [InlineData("ko", "BC-E-001", "사용자 ID 또는 비밀번호가 올바르지 않습니다.")]
    [InlineData("en", "BC-E-001", "The user ID or password is incorrect.")]
    [InlineData("ko", "XX-E-999", "요청을 처리하지 못했습니다. 잠시 후 다시 시도하세요.")]
    [InlineData("en", null, "The request could not be processed. Please try again later.")]
    public void Get_Code_ReturnsMessageInUserLanguageOrUnknown(string culture, string? code, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var messages = new ProblemMessages(factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>());

            messages.Get(new ApiProblem(422, code, [])).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory, Trait("Id", "CORE-005-WT-01")]
    [InlineData("ko", "VALID_FLAG 코드 값 'X'이(가) 올바르지 않습니다.")]
    [InlineData("en", "'X' is not a valid VALID_FLAG code.")]
    public void Get_InvalidCodeWithArgs_FormatsArgs(string culture, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var messages = new ProblemMessages(factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>());

            messages.Get(new ApiProblem(422, "Common-E-002", ["VALID_FLAG", "X"])).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory, Trait("Id", "CORE-004-WT-10")]
    [InlineData("en", "The user ID or password is incorrect.")]
    [InlineData("ko", "사용자 ID 또는 비밀번호가 올바르지 않습니다.")]
    public async Task LoginPost_ApiReturns401Problem_ShowsMessageForCode(string culture, string expected)
    {
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"status":401,"code":"BC-E-001"}""", null, "application/problem+json"),
        };

        var html = await PostLoginAsync(culture);

        WebUtility.HtmlDecode(html).Should().Contain(expected);
    }

    [Fact, Trait("Id", "CORE-004-WT-11")]
    public async Task LoginPost_ApiReturns500WithoutCode_ShowsUnknownMessage()
    {
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var html = await PostLoginAsync("en");

        WebUtility.HtmlDecode(html).Should().Contain("The request could not be processed. Please try again later.");
    }

    private async Task<string> PostLoginAsync(string culture)
    {
        var browser = factory.CreateBrowser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync("/bc/login"));

        var response = await browser.PostAsync("/bc/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "wrong",
            ["__RequestVerificationToken"] = token,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }
}

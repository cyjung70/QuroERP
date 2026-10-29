using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Code management screen /bc/code (BC-UC-008, TASK-BC-016) against a fake code API.</summary>
public class CodeScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeCodeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public CodeScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<CodeMinorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeMajorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UT-910")]
    public void Open_ShowsNameColumnsPerLanguageInUse_EnFirst_AndSearchesNothingYet()
    {
        var page = RenderAs("admin");

        page.FindAll("th").Select(h => h.TextContent.Trim()).Should().ContainInOrder("코드", "이름 (English)", "이름 (한국어)", "정렬 순서", "사용");
        page.Find("[data-testid=code-search-major-code]").GetAttribute("aria-required").Should().Be("true");
        _api.MinorSearches.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-910")]
    public void Search_WithoutAMajor_AsksForIt()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=code-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-message]").TextContent.Should().Be("Major 코드을(를) 입력하세요."));
        _api.MinorSearches.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-008-T03")]
    public void Search_ByMajorCode_ShowsItsCodesInUseOrNot_AndTheMajorName()
    {
        var page = SearchUnit(RenderAs("admin"));

        Values(page, "code-grid-minor-code").Should().Equal("EA", "BOX");
        Values(page, "code-grid-name-en").Should().Equal("Each", "Box");
        Values(page, "code-grid-name-ko").Should().Equal("개", "");
        page.FindAll("[data-testid=code-grid-use-yn]").Select(c => c.HasAttribute("checked")).Should().Equal(true, false);
        page.Find("[data-testid=code-search-major-name]").GetAttribute("value").Should().Be("단위");
        _api.MinorSearches.Should().Equal("UNIT");
    }

    [Fact, Trait("Id", "BC-UC-008-T03")]
    public void MajorLookup_FillsTheCodeAndName()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=code-search-find-major]").Click();
        page.WaitForElement("[data-testid=code-major-lookup] tbody tr");
        page.FindAll("[data-testid=code-major-lookup] tbody tr")[1].QuerySelector("button")!.Click();

        page.Find("[data-testid=code-search-major-code]").GetAttribute("value").Should().Be("UNIT");
        page.Find("[data-testid=code-search-major-name]").GetAttribute("value").Should().Be("단위");
        page.FindAll("[data-testid=code-major-lookup]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-912")]
    public void MinorCode_IsLockedOnLoadedRows_AndOpenOnNewOnes_EnNameRequired()
    {
        var page = SearchUnit(RenderAs("admin"));

        page.Find("[data-testid=code-toolbar-new]").Click();

        var codes = page.FindAll("[data-testid=code-grid-minor-code]");
        codes.Take(2).Should().OnlyContain(c => c.HasAttribute("readonly"));
        codes[2].HasAttribute("readonly").Should().BeFalse();
        page.FindAll("[data-testid=code-grid-name-en]")[2].GetAttribute("aria-required").Should().Be("true");
        page.FindAll("[data-testid=code-grid-name-ko]")[2].HasAttribute("aria-required").Should().BeFalse();
        page.FindAll("[data-testid=code-grid-use-yn]")[2].HasAttribute("checked").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UC-008-T01")]
    public void AddChangeAndDelete_AreSavedTogether_ForTheMajorThatWasRead()
    {
        var page = SearchUnit(RenderAs("admin"));

        page.FindAll("[data-testid=code-grid-name-ko]")[1].Change("상자");
        page.FindAll("[data-testid=code-grid-sort-order]")[1].Change("30");
        page.FindAll("[data-testid=code-grid-delete-row]")[0].Click();
        page.Find("[data-testid=code-toolbar-new]").Click();
        page.FindAll("[data-testid=code-grid-minor-code]")[2].Change("pc");
        page.FindAll("[data-testid=code-grid-name-en]")[2].Change("Piece");
        page.FindAll("[data-testid=code-grid-sort-order]")[2].Change("5");
        page.FindAll("[data-testid=code-grid-attr1]")[2].Change("P");
        page.Find("[data-testid=code-search-major-code]").Change("TERMS");

        page.Find("[data-testid=code-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.MajorCode.Should().Be("UNIT");
        saved.Inserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            MinorCode = "PC", SortOrder = 5, UseYn = true, Attr1 = "P", Names = new Dictionary<string, string?> { ["en"] = "Piece", ["ko"] = null },
        });
        saved.Updated.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 12L, MinorCode = "BOX", SortOrder = 30, UseYn = false, UpdatedAt = Read, Names = new Dictionary<string, string?> { ["en"] = "Box", ["ko"] = "상자" },
        });
        saved.Deleted.Should().Equal(new CodeMinorDelete(11, Read));
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public void NewRow_WithoutTheEnName_IsNotSent()
    {
        var page = SearchUnit(RenderAs("admin"));
        page.Find("[data-testid=code-toolbar-new]").Click();
        page.FindAll("[data-testid=code-grid-minor-code]")[2].Change("PC");
        page.FindAll("[data-testid=code-grid-sort-order]")[2].Change("5");

        page.Find("[data-testid=code-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-message]").TextContent.Should().Be("이름 (English)을(를) 입력하세요."));
        _api.Saved.Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UC-008-T02")]
    [InlineData("SD-E-911", "코드 PC은(는) 비었거나, 영문 대문자·숫자·_로 20자 이하가 아니거나, 이미 있거나, 영어 이름이 없습니다.")]
    [InlineData("SD-E-913", "코드 PC의 정렬 순서는 1 이상 정수이고 겹치지 않아야 합니다.")]
    [InlineData("SD-E-914", "코드 PC은(는) 업무 데이터가 쓰고 있어 삭제할 수 없습니다. 사용 안 함으로 바꾸세요.")]
    [InlineData("SD-E-916", "코드 PC은(는) 시스템이 쓰는 코드라 삭제하거나 사용 안 함으로 바꿀 수 없습니다.")]
    [InlineData("SD-E-912", "코드 PC의 값은 바꿀 수 없습니다.")]
    public void FailingRow_IsNamedInTheMessage_AndTheGridKeepsTheChanges(string code, string message)
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, $$"""{"status":422,"code":"{{code}}","args":["PC"]}""");
        var page = SearchUnit(RenderAs("admin"));
        page.FindAll("[data-testid=code-grid-name-en]")[0].Change("Each one");

        page.Find("[data-testid=code-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-message]").TextContent.Should().Be(message));
        Values(page, "code-grid-name-en")[0].Should().Be("Each one");
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = SearchUnit(RenderAs("admin"));
        page.FindAll("[data-testid=code-grid-name-en]")[0].Change("Each one");

        page.Find("[data-testid=code-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanOnlyRead()
    {
        var page = SearchUnit(RenderAs("user"));

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().NotContain(["code-toolbar-new", "code-toolbar-save"]);
        page.FindAll("[data-testid=code-grid-name-en]").Should().OnlyContain(c => c.HasAttribute("readonly"));
        page.FindAll("[data-testid=code-grid-delete-row]").Should().BeEmpty();
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<Code> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Code>();
        page.WaitForElement("[data-testid=code-search-major-code]");
        return page;
    }

    private static IRenderedComponent<Code> SearchUnit(IRenderedComponent<Code> page)
    {
        page.Find("[data-testid=code-search-major-code]").Change("unit");
        page.Find("[data-testid=code-toolbar-search]").Click();
        page.WaitForAssertion(() => Values(page, "code-grid-minor-code").Should().NotBeEmpty());
        return page;
    }

    private static List<string> Values(IRenderedComponent<Code> page, string testId) =>
        page.FindAll($"[data-testid={testId}]").Select(e => e.GetAttribute("value") ?? "").ToList();

    /// <summary>Languages ko then en; Majors TERMS and UNIT; UNIT holds EA (Each/개) and BOX (Box, not in use).</summary>
    private sealed class FakeCodeApi : HttpMessageHandler
    {
        private static readonly CodeMajorDto[] Majors =
        [
            new(2, "TERMS", "결제 조건", new Dictionary<string, string> { ["en"] = "Terms", ["ko"] = "결제 조건" }, true, 1, Read),
            new(1, "UNIT", "단위", new Dictionary<string, string> { ["en"] = "Unit", ["ko"] = "단위" }, true, 2, Read),
        ];

        private static readonly CodeMinorDto[] Units =
        [
            new(11, "EA", "개", new Dictionary<string, string> { ["en"] = "Each", ["ko"] = "개" }, 10, true, null, null, Read),
            new(12, "BOX", "Box", new Dictionary<string, string> { ["en"] = "Box" }, 20, false, null, null, Read),
        ];

        public List<string?> MinorSearches { get; } = [];

        public List<SaveCodeMinorsRequest> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [new("ko", 1), new("en", 2)], [], [], []));
            }
            if (path == "/" + CodeMajorRoutes.Base)
            {
                var prefix = query["majorCode"];
                return Json<IReadOnlyList<CodeMajorDto>>(Majors.Where(m => prefix is null || m.MajorCode.StartsWith(prefix, StringComparison.Ordinal)).ToList());
            }
            if (path != "/" + CodeMinorRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<SaveCodeMinorsRequest>(cancellationToken))!);
                return Json<IReadOnlyList<CodeMinorDto>>(Units);
            }
            MinorSearches.Add(query["majorCode"]);
            return Json<IReadOnlyList<CodeMinorDto>>(query["majorCode"] == "UNIT" ? Units : []);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

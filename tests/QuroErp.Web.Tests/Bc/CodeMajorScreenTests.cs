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

/// <summary>Major code screen /bc/code-major (BC-UC-023, TASK-BC-046) against a fake code API.</summary>
public class CodeMajorScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeCodeMajorApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public CodeMajorScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<CodeMajorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UC-023-T07")]
    public void Open_ShowsEveryMajor_WithNamesPerLanguageInUse_EnFirst_AndMinorCount()
    {
        var page = RenderAs("admin");

        page.FindAll("th").Select(h => h.TextContent.Trim()).Should().ContainInOrder("Major 코드", "이름 (English)", "이름 (한국어)", "사용", "Minor 수");
        Values(page, "code-major-grid-major-code").Should().Equal("TEMP", "TERMS", "UNIT");
        Values(page, "code-major-grid-name-ko").Should().Equal("", "결제 조건", "단위");
        page.FindAll("tbody tr").Select(r => r.Children[^2].TextContent.Trim()).Should().Contain(["0", "1", "2"]);
        _api.Searches.Should().Equal((string?)null);
    }

    [Fact, Trait("Id", "BC-UC-023-T07")]
    public void Search_ByCodePrefix_Filters_AndShowsTheNameOfTheMatchingMajor()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=code-major-search-major-code]").Change("terms");
        page.Find("[data-testid=code-major-toolbar-search]").Click();

        page.WaitForAssertion(() => Values(page, "code-major-grid-major-code").Should().Equal("TERMS"));
        _api.Searches.Last().Should().Be("TERMS");
        page.Find("[data-testid=code-major-search-name]").GetAttribute("value").Should().Be("결제 조건");
    }

    [Fact, Trait("Id", "BC-UC-023-T02")]
    public void MajorCode_IsLockedOnLoadedRows_AndOpenOnNewOnes_EnNameRequired()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=code-major-toolbar-new]").Click();

        var codes = page.FindAll("[data-testid=code-major-grid-major-code]");
        codes.Take(3).Should().OnlyContain(c => c.HasAttribute("readonly"));
        codes[3].HasAttribute("readonly").Should().BeFalse();
        page.FindAll("[data-testid=code-major-grid-name-en]")[3].GetAttribute("aria-required").Should().Be("true");
        page.FindAll("[data-testid=code-major-grid-name-ko]")[3].HasAttribute("aria-required").Should().BeFalse();
        page.FindAll("[data-testid=code-major-grid-use-yn]")[3].HasAttribute("checked").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UC-023-T07")]
    public void AddChangeAndDelete_AreSavedTogether_WithNamesInLanguagesInUseOnly()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=code-major-grid-name-ko]")[0].Change("임시");
        page.FindAll("[data-testid=code-major-grid-use-yn]")[0].Change(false);
        page.FindAll("[data-testid=code-major-grid-delete-row]")[1].Click();
        page.Find("[data-testid=code-major-toolbar-new]").Click();
        page.FindAll("[data-testid=code-major-grid-major-code]")[3].Change("ship_via");
        page.FindAll("[data-testid=code-major-grid-name-en]")[3].Change("Ship Via");

        page.Find("[data-testid=code-major-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-major-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Inserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            MajorCode = "SHIP_VIA",
            UseYn = true,
            Names = new Dictionary<string, string?> { ["en"] = "Ship Via", ["ko"] = null },
        });
        saved.Updated.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 3L,
            MajorCode = "TEMP",
            UseYn = false,
            UpdatedAt = Read,
            Names = new Dictionary<string, string?> { ["en"] = "Temporary", ["ko"] = "임시" },
        });
        saved.Deleted.Should().Equal(new CodeMajorDelete(2, Read));
    }

    [Fact, Trait("Id", "BC-UC-023-T01")]
    public void NewRow_WithoutCodeOrEnName_IsNotSent()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=code-major-toolbar-new]").Click();

        page.Find("[data-testid=code-major-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-major-message]").TextContent.Should().Be("Major 코드을(를) 입력하세요."));
        _api.Saved.Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UC-023-T05")]
    [InlineData("SD-E-901", "Major 코드 TERMS은(는) 비었거나, 영문 대문자·숫자·_로 30자 이하가 아니거나, 이미 있거나, 영어 이름이 없습니다.")]
    [InlineData("SD-E-902", "Major 코드 TERMS의 값은 바꿀 수 없습니다.")]
    [InlineData("SD-E-903", "Major 코드 TERMS에 Minor 코드가 있어 삭제할 수 없습니다. 먼저 Minor 코드를 지우거나 사용 안 함으로 바꾸세요.")]
    [InlineData("SD-E-904", "Major 코드 TERMS은(는) 시스템이 쓰는 코드라 삭제하거나 사용 안 함으로 바꿀 수 없습니다.")]
    public void FailingRow_IsNamedInTheMessage_AndTheGridKeepsTheChanges(string code, string message)
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, $$"""{"status":422,"code":"{{code}}","args":["TERMS"]}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=code-major-grid-name-en]")[1].Change("Payment Terms");

        page.Find("[data-testid=code-major-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-major-message]").TextContent.Should().Be(message));
        Values(page, "code-major-grid-name-en")[1].Should().Be("Payment Terms");
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=code-major-grid-name-en]")[0].Change("Temp");

        page.Find("[data-testid=code-major-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=code-major-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Id", "CORE-051-WT-02")]
    public void UserWithoutFullAccess_CanOnlyRead_NewSaveAndRowButtonsAreDisabledNotHidden()
    {
        var page = RenderAs("user");
        var rows = page.FindAll("[data-testid=code-major-grid] tbody tr").Count;

        page.Find("[data-testid=code-major-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=code-major-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=code-major-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=code-major-toolbar-export]").HasAttribute("disabled").Should().BeFalse();
        page.FindAll("[data-testid=code-major-grid-delete-row]").Should().NotBeEmpty().And.OnlyContain(b => b.HasAttribute("disabled"));
        page.FindAll("[data-testid=code-major-grid-name-en]").Should().OnlyContain(c => c.HasAttribute("readonly"));

        // The handlers do nothing either
        page.Find("[data-testid=code-major-toolbar-new]").Click();
        page.FindAll("[data-testid=code-major-grid-delete-row]")[0].Click();
        page.Find("[data-testid=code-major-toolbar-save]").Click();
        page.FindAll("[data-testid=code-major-grid] tbody tr").Should().HaveCount(rows);
        page.FindAll("[data-state=D]").Should().BeEmpty();
        _api.Saved.Should().BeEmpty();
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<CodeMajor> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<CodeMajor>();
        page.WaitForAssertion(() => Values(page, "code-major-grid-major-code").Should().NotBeEmpty());
        return page;
    }

    private static List<string> Values(IRenderedComponent<CodeMajor> page, string testId) =>
        page.FindAll($"[data-testid={testId}]").Select(e => e.GetAttribute("value") ?? "").ToList();

    /// <summary>Languages ko then en; TEMP (no Minor codes), TERMS (1, with a name in ja, which is not in use) and UNIT (2).</summary>
    private sealed class FakeCodeMajorApi : HttpMessageHandler
    {
        private static readonly CodeMajorDto[] Majors =
        [
            new(3, "TEMP", "Temporary", new Dictionary<string, string> { ["en"] = "Temporary" }, true, 0, Read),
            new(2, "TERMS", "결제 조건", new Dictionary<string, string> { ["en"] = "Terms", ["ko"] = "결제 조건", ["ja"] = "支払条件" }, true, 1, Read),
            new(1, "UNIT", "단위", new Dictionary<string, string> { ["en"] = "Unit", ["ko"] = "단위" }, true, 2, Read),
        ];

        public List<string?> Searches { get; } = [];

        public List<SaveCodeMajorsRequest> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [new("ko", 1), new("en", 2)], [], [], []));
            }
            if (path != "/" + CodeMajorRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<SaveCodeMajorsRequest>(cancellationToken))!);
                return Json<IReadOnlyList<CodeMajorDto>>(Majors);
            }
            var prefix = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["majorCode"];
            Searches.Add(prefix);
            return Json<IReadOnlyList<CodeMajorDto>>(Majors.Where(m => prefix is null || m.MajorCode.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

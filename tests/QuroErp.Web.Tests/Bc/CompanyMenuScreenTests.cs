using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Menus;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Company menu screen /bc/company-menu (BC-UC-006, TASK-BC-012) against a fake menu API.</summary>
public class CompanyMenuScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeMenuApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public CompanyMenuScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<MenuApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UC-006-T03")]
    public void Open_ShowsEveryMenu_WithTypeNames()
    {
        var page = RenderAs("admin");

        MenuCodes(page).Should().Equal("BASE", "ITEM", "USER");
        Values(page, "company-menu-grid-parent-menu-code").Should().Equal("", "BASE", "BASE");
        page.Find("[data-testid=company-menu-grid-type] option[selected]").TextContent.Should().Be("메뉴 그룹");
        page.FindAll("[data-testid=company-menu-grid-module] option").Select(o => o.GetAttribute("value")).Should().Contain(["bc", "mm", "sd", "fi", "rpt"]);
        _api.Searches.Should().Equal((string?)null);
    }

    [Fact, Trait("Id", "BC-UC-006-T03")]
    public void Search_ByMenuCode_FiltersAndShowsTheNameOfTheMatchingMenu()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=company-menu-search-menu-code]").Change("item");
        page.Find("[data-testid=company-menu-toolbar-search]").Click();

        page.WaitForAssertion(() => MenuCodes(page).Should().Equal("ITEM"));
        _api.Searches.Last().Should().Be("ITEM");
        page.Find("[data-testid=company-menu-search-name]").GetAttribute("value").Should().Be("Items");
    }

    [Fact, Trait("Id", "BC-UT-201")]
    public void MenuCode_IsLockedOnLoadedRows_AndOpenOnNewOnes()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=company-menu-toolbar-new]").Click();

        var codes = page.FindAll("[data-testid=company-menu-grid-menu-code]");
        codes.Take(3).Should().OnlyContain(c => c.HasAttribute("readonly") || c.HasAttribute("disabled"));
        codes[3].HasAttribute("readonly").Should().BeFalse();
        Values(page, "company-menu-grid-valid-flag")[3].Should().Be("Y");
    }

    [Fact, Trait("Id", "BC-UC-006-T01")]
    public void AddChangeAndDelete_AreSavedTogether()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=company-menu-grid-name]")[1].Change("Item master");
        page.FindAll("[data-testid=company-menu-grid-delete-row]")[2].Click();
        page.Find("[data-testid=company-menu-toolbar-new]").Click();
        page.FindAll("[data-testid=company-menu-grid-menu-code]")[3].Change("whs");
        page.FindAll("[data-testid=company-menu-grid-name]")[3].Change("Warehouses");
        page.FindAll("[data-testid=company-menu-grid-program-id]")[3].Change("/mm/warehouse");
        page.FindAll("[data-testid=company-menu-grid-module]")[3].Change("mm");
        page.FindAll("[data-testid=company-menu-grid-parent-menu-code]")[3].Change("base");

        page.Find("[data-testid=company-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-menu-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Inserted.Should().Equal(new MenuInsert("WHS", "Warehouses", "P", "/mm/warehouse", "mm", "BASE", "Y"));
        saved.Updated.Should().Equal(new MenuUpdate(2, "Item master", "P", "/bc/item", "bc", "BASE", "Y", Read));
        saved.Deleted.Should().Equal(new MenuDelete(3, Read));
    }

    [Fact, Trait("Id", "BC-UC-006-T01")]
    public void NewRow_WithoutRequiredValues_IsNotSent()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-menu-toolbar-new]").Click();

        page.Find("[data-testid=company-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-menu-message]").TextContent.Should().Contain("을(를) 입력하세요."));
        _api.Saved.Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UC-006-T02")]
    [InlineData("BC-E-205", "메뉴 ITEM의 상위 메뉴는 비우거나, 있는 메뉴 그룹이어야 합니다. 자기 자신이나 하위 메뉴는 상위로 둘 수 없습니다.")]
    [InlineData("BC-E-207", "메뉴 ITEM은(는) 역할에 배정되었거나 하위 메뉴가 있어 삭제할 수 없습니다. 사용 안 함으로 바꾸세요.")]
    [InlineData("BC-E-203", "메뉴 ITEM의 유형이 올바르지 않습니다. 프로그램·팝업은 프로그램 ID가 있어야 하고, 메뉴 그룹은 비워야 합니다.")]
    public void FailingRow_IsNamedInTheMessage_AndTheGridKeepsTheChanges(string code, string message)
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, $$"""{"status":422,"code":"{{code}}","args":["ITEM"]}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=company-menu-grid-name]")[1].Change("Changed");

        page.Find("[data-testid=company-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-menu-message]").TextContent.Should().Be(message));
        Values(page, "company-menu-grid-name")[1].Should().Be("Changed");
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=company-menu-grid-name]")[0].Change("Changed");

        page.Find("[data-testid=company-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-menu-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanOnlyRead()
    {
        var page = RenderAs("user");

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().NotContain(["company-menu-toolbar-new", "company-menu-toolbar-save"]);
        page.FindAll("[data-testid=company-menu-grid-name]").Should().OnlyContain(c => c.HasAttribute("readonly") || c.HasAttribute("disabled"));
        page.FindAll("[data-testid=company-menu-grid-delete-row]").Should().BeEmpty();
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<CompanyMenu> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<CompanyMenu>();
        page.WaitForAssertion(() => MenuCodes(page).Should().NotBeEmpty());
        return page;
    }

    private static List<string> MenuCodes(IRenderedComponent<CompanyMenu> page) => Values(page, "company-menu-grid-menu-code");

    private static List<string> Values(IRenderedComponent<CompanyMenu> page, string testId) =>
        page.FindAll($"[data-testid={testId}]").Select(e => e.TagName == "SELECT"
            ? e.QuerySelector("option[selected]")?.GetAttribute("value") ?? ""
            : e.GetAttribute("value") ?? "").ToList();

    /// <summary>BASE (group) holding ITEM and USER.</summary>
    private sealed class FakeMenuApi : HttpMessageHandler
    {
        private static readonly MenuDto[] Menus =
        [
            new(1, "BASE", "Base", "M", null, "bc", null, "Y", Read),
            new(2, "ITEM", "Items", "P", "/bc/item", "bc", "BASE", "Y", Read),
            new(3, "USER", "Users", "P", "/bc/user", "bc", "BASE", "Y", Read),
        ];

        public List<string?> Searches { get; } = [];

        public List<SaveMenusRequest> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json<IReadOnlyList<CodeItemDto>>(path.EndsWith("VALID_FLAG", StringComparison.Ordinal)
                    ? [new("Y", "사용"), new("N", "사용 안 함")]
                    : []);
            }
            if (path != "/" + MenuRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<SaveMenusRequest>(cancellationToken))!);
                return Json<IReadOnlyList<MenuDto>>(Menus);
            }
            var query = request.RequestUri.Query;
            var menuCode = query.StartsWith("?menuCode=", StringComparison.Ordinal) ? Uri.UnescapeDataString(query["?menuCode=".Length..]) : null;
            Searches.Add(menuCode);
            return Json<IReadOnlyList<MenuDto>>(Menus.Where(m => menuCode is null || m.MenuCode.StartsWith(menuCode, StringComparison.Ordinal)).ToList());
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

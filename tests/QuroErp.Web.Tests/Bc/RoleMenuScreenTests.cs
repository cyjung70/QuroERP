using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Roles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Role menu permission screen /bc/role-menu (BC-UC-005, TASK-BC-010) against a fake role API.</summary>
public class RoleMenuScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeRoleApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public RoleMenuScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<RoleApiClient>(http => http.BaseAddress = new Uri("http://api.test")).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UT-211")]
    public void Open_ShowsTheFirstRoleInUse_AndItsMenus()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=role-menu-search-role] option[selected]").GetAttribute("value").Should().Be("1");
        MenuCodes(page).Should().Equal("ITEM", "ROLEMENU");
        page.Find("[data-testid=role-menu-grid] tbody tr").TextContent.Should().Contain("프로그램");
    }

    [Fact, Trait("Id", "BC-UC-005-T03")]
    public void Search_ByAnotherRole_ShowsThatRolesMenus()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=role-menu-search-role]").Change("2");
        page.Find("[data-testid=role-menu-toolbar-search]").Click();

        page.WaitForAssertion(() => MenuCodes(page).Should().Equal("ITEM"));
        page.Find("[data-testid=role-menu-grid-full-access]").HasAttribute("checked").Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UC-005-T01")]
    public void AddChangeAndDelete_AreSavedTogether()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=role-menu-toolbar-new]").Click();
        page.WaitForAssertion(() => page.LookupRows("role-menu-menu-lookup").Should().ContainSingle());
        page.PickLookupRow<MenuOptionDto, RoleMenu>("role-menu-menu-lookup", 0);
        page.WaitForAssertion(() => MenuCodes(page).Should().Equal("ITEM", "ROLEMENU", "USER"));
        page.FindAll("[data-testid=role-menu-grid-full-access]")[2].Change(true);
        page.FindAll("[data-testid=role-menu-grid-full-access]")[0].Change(false);
        page.FindAll("[data-testid=role-menu-grid-delete-row]")[1].Click();

        page.Find("[data-testid=role-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=role-menu-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Inserted.Should().Equal(new RoleMenuInsert(102, true));
        saved.Updated.Should().Equal(new RoleMenuUpdate(101, false, Read));
        saved.Deleted.Should().Equal(new RoleMenuDelete(100, Read));
        _api.SavedRoleId.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-212")]
    public void AddDialog_LeavesOutMenusAlreadyOnTheGrid()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=role-menu-toolbar-new]").Click();
        page.WaitForAssertion(() => page.LookupRows("role-menu-menu-lookup").Should().NotBeEmpty());
        page.PickLookupRow<MenuOptionDto, RoleMenu>("role-menu-menu-lookup", 0);

        page.Find("[data-testid=role-menu-toolbar-new]").Click();

        page.WaitForAssertion(() => page.LookupRows("role-menu-menu-lookup").Should().BeEmpty());
    }

    [Fact, Trait("Id", "BC-UC-005-T02")]
    public void Duplicate_IsShownWithTheMenuCode()
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-212","args":["101"]}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=role-menu-grid-full-access]")[0].Change(false);

        page.Find("[data-testid=role-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=role-menu-message]").TextContent.Should().Be("메뉴 ITEM은(는) 이미 이 역할에 있습니다."));
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public void LosingTheLastManager_ShowsBcE216()
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-216"}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=role-menu-grid-full-access]")[1].Change(false);

        page.Find("[data-testid=role-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=role-menu-message]").TextContent.Should().StartWith("역할별 메뉴 권한 화면을 Full Access로"));
    }

    [Fact, Trait("Id", "BC-UT-213")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = RenderAs("admin");
        page.FindAll("[data-testid=role-menu-grid-full-access]")[0].Change(false);

        page.Find("[data-testid=role-menu-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=role-menu-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Id", "CORE-051-WT-05")]
    public void UserWithoutFullAccess_CanOnlyRead_NewSaveAndRowButtonsAreDisabledNotHidden()
    {
        var page = RenderAs("user");
        var rows = page.FindAll("[data-testid=role-menu-grid] tbody tr").Count;

        page.Find("[data-testid=role-menu-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=role-menu-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=role-menu-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=role-menu-toolbar-export]").HasAttribute("disabled").Should().BeFalse();
        page.FindAll("[data-testid=role-menu-grid-delete-row]").Should().NotBeEmpty().And.OnlyContain(b => b.HasAttribute("disabled"));
        page.FindAll("[data-testid=role-menu-grid-full-access]").Should().OnlyContain(c => c.HasAttribute("disabled"));

        // The handlers do nothing either
        page.Find("[data-testid=role-menu-toolbar-new]").Click();
        page.FindAll("[data-testid=role-menu-grid-delete-row]")[0].Click();
        page.Find("[data-testid=role-menu-toolbar-save]").Click();
        page.FindAll("[data-testid=role-menu-grid] tbody tr").Should().HaveCount(rows);
        page.FindAll("[data-state=D]").Should().BeEmpty();
        _api.Saved.Should().BeEmpty();
    }

    private IRenderedComponent<RoleMenu> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<RoleMenu>();
        page.WaitForAssertion(() => MenuCodes(page).Should().NotBeEmpty());
        return page;
    }

    private static List<string> MenuCodes(IRenderedComponent<RoleMenu> page) =>
        page.FindAll("[data-testid=role-menu-grid] tbody tr").Select(r => r.QuerySelectorAll("td")
            .Select(c => c.TextContent.Trim()).First(t => t is "ITEM" or "ROLEMENU" or "USER")).ToList();

    /// <summary>Roles admin (1) with ITEM and ROLEMENU at Full Access, user (2) reading ITEM. USER (102) is not assigned to admin.</summary>
    private sealed class FakeRoleApi : HttpMessageHandler
    {
        public List<SaveRoleMenusRequest> Saved { get; } = [];

        public long? SavedRoleId { get; private set; }

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + RoleRoutes.Base)
            {
                return Json<IReadOnlyList<RoleDto>>([new(1, "admin"), new(2, "user")]);
            }
            if (request.Method == HttpMethod.Get && path == "/api/sys/roles/1/menus")
            {
                return Json<IReadOnlyList<RoleMenuDto>>(
                    [new(101, "ITEM", "Item", "P", "bc", true, Read), new(100, "ROLEMENU", "Role menu", "P", "bc", true, Read)]);
            }
            if (request.Method == HttpMethod.Get && path == "/api/sys/roles/2/menus")
            {
                return Json<IReadOnlyList<RoleMenuDto>>([new(101, "ITEM", "Item", "P", "bc", false, Read)]);
            }
            if (path == "/api/sys/roles/1/unassigned-menus")
            {
                return Json<IReadOnlyList<MenuOptionDto>>([new(102, "USER", "User", "P", "bc")]);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<SaveRoleMenusRequest>(cancellationToken))!);
                SavedRoleId = long.Parse(path.Split('/')[4], CultureInfo.InvariantCulture);
                return Json<IReadOnlyList<RoleMenuDto>>([]);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

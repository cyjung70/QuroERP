using System.Globalization;
using Bunit;
using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;

namespace QuroErp.Web.Tests.Layout;

/// <summary>App shell top bar and side menu (TASK-CORE-037, app-shell.md 2, 3).</summary>
public class ShellTests : BunitContext
{
    private static readonly AuthMenu[] AdminMenus =
    [
        new("G1BA", "Master Data", null, "bc", null, null, "M"),
        new("G1BA01", "Items", "/bc/item", "bc", "G1BA", "Y", "P"),
        new("G1BA02", "Codes", "/bc/code", "bc", "G1BA", "Y", "P"),
        new("G1BA90", "Item popup", "/bc/item-popup", "bc", "G1BA", "Y", "U"),
        new("G5AD", "Administration", null, "bc", null, null, "M"),
        new("G5AD02", "Currencies", "/bc/currency", "bc", "G5AD", "Y", "P"),
    ];

    private static readonly AuthMenu[] ClerkMenus =
    [
        new("G1BA", "Master Data", null, "bc", null, null, "M"),
        new("G1BA01", "Items", "/bc/item", "bc", "G1BA", null, "P"),
    ];

    public ShellTests()
    {
        UseCulture("ko");
        Services.AddWebLocalization();
        AddAuthorization().SetAuthorized("kim");
    }

    private static ShellProfile Profile(IReadOnlyList<AuthMenu> menus, string? roleName = null) =>
        new(new MeResponse("kim", "Kim", "ADMIN", "ko", false, null, null, null, menus, "Quro Co.", roleName), ["ko", "en"]);

    private static void UseCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);

    [Fact, Trait("Id", "CORE-037-WT-01")]
    public void Nav_ShowsGroupsAndLinksOfTheRole_InOrderWithResourceNames()
    {
        var nav = Render<ShellNav>(p => p.Add(x => x.Profile, Profile(AdminMenus)).Add(x => x.CurrentPath, "/"));

        nav.FindAll("[data-testid^=shell-nav-group-]").Select(g => g.GetAttribute("data-testid"))
            .Should().Equal("shell-nav-group-g1ba", "shell-nav-group-g5ad");
        nav.Find("[data-testid=shell-nav-group-g1ba] .shell-nav-label").TextContent.Should().Be("기준정보");
        nav.Find("[data-testid=shell-nav-item-g1ba01]").GetAttribute("href").Should().Be("/bc/item");
        nav.Find("[data-testid=shell-nav-item-g1ba01]").TextContent.Should().Be("품목");
        nav.FindAll("[data-testid=shell-nav-item-g1ba90]").Should().BeEmpty("U menus are popups, not menu items");
    }

    [Fact, Trait("Id", "CORE-037-WT-02")]
    public void Nav_OtherRole_ShowsOnlyItsMenus()
    {
        var nav = Render<ShellNav>(p => p.Add(x => x.Profile, Profile(ClerkMenus)));

        nav.FindAll("[data-testid^=shell-nav-group-]").Should().ContainSingle();
        nav.FindAll("[data-testid^=shell-nav-item-]").Select(a => a.GetAttribute("data-testid")).Should().Equal("shell-nav-item-g1ba01");
    }

    [Fact, Trait("Id", "CORE-037-WT-03")]
    public void Nav_CurrentScreen_IsMarkedAndItsGroupFlagged()
    {
        var nav = Render<ShellNav>(p => p.Add(x => x.Profile, Profile(AdminMenus)).Add(x => x.CurrentPath, "/bc/currency"));

        nav.Find("[data-testid=shell-nav-item-g5ad02]").GetAttribute("aria-current").Should().Be("page");
        nav.Find("[data-testid=shell-nav-item-g1ba01]").HasAttribute("aria-current").Should().BeFalse();
        nav.Find("[data-testid=shell-nav-group-g5ad]").GetAttribute("data-current").Should().Be("true");
        nav.Find("[data-testid=shell-nav-group-g1ba]").HasAttribute("data-current").Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-037-WT-04")]
    public void Nav_NoMenusOrNoProfile_ShowsTheEmptyText()
    {
        Render<ShellNav>(p => p.Add(x => x.Profile, Profile([]))).Find("[data-testid=shell-nav-empty]")
            .TextContent.Should().Be("사용할 수 있는 메뉴가 없습니다.");
        Render<ShellNav>().FindAll("[data-testid=shell-nav-empty]").Should().ContainSingle();
    }

    [Fact, Trait("Id", "CORE-037-WT-05")]
    public void Nav_English_UsesEnglishNamesAndCollapseButton()
    {
        UseCulture("en");

        var nav = Render<ShellNav>(p => p.Add(x => x.Profile, Profile(AdminMenus)));

        nav.Find("[data-testid=shell-nav-group-g1ba] .shell-nav-label").TextContent.Should().Be("Master Data");
        nav.Find("[data-testid=shell-nav-item-g5ad02]").TextContent.Should().Be("Currencies");
        nav.Find("[data-testid=shell-nav-collapse]").GetAttribute("aria-label").Should().Be("Collapse menu");
    }

    [Fact, Trait("Id", "CORE-037-WT-06")]
    public void TopBar_ShowsItemsInOrder_WithCompanyRoleCodeAndForms()
    {
        var top = Render<ShellTopBar>(p => p.Add(x => x.Profile, Profile(AdminMenus)));

        top.FindAll("[data-testid]").Select(e => e.GetAttribute("data-testid"))
            .Where(id => id!.StartsWith("shell-", StringComparison.Ordinal) && id != "shell-top" || id == "user-menu")
            .Should().Equal("shell-menu-toggle", "shell-logo", "shell-company", "shell-language", "shell-role", "user-menu", "shell-logout");
        top.Find("[data-testid=shell-company]").TextContent.Should().Be("Quro Co.");
        top.Find("[data-testid=shell-role]").TextContent.Should().Be("ADMIN", "roles have no name yet, so the code shows");
        var language = top.Find("[data-testid=shell-language]");
        language.Closest("form")!.GetAttribute("action").Should().Be(WebLocalization.CulturePath);
        language.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).Should().Equal("ko", "en");
        language.QuerySelector("option[selected]")!.GetAttribute("value").Should().Be("ko");
        top.Find("[data-testid=shell-logout]").Closest("form")!.GetAttribute("action").Should().Be(WebAuthentication.LogoutPath);
    }

    [Fact, Trait("Id", "CORE-037-WT-07")]
    public void TopBar_RoleName_WinsOverCode_AndUserMenuCarriesCompanyAndRole()
    {
        var top = Render<ShellTopBar>(p => p.Add(x => x.Profile, Profile(AdminMenus, roleName: "Administrator")));

        top.Find("[data-testid=shell-role]").TextContent.Should().Be("Administrator");
        top.Find("[data-testid=user-menu-company]").TextContent.Should().Be("Quro Co.");
        top.Find("[data-testid=user-menu-role]").TextContent.Should().Be("Administrator");
        top.Find("[data-testid=user-menu] summary").TextContent.Should().Be("kim");
    }

    [Fact, Trait("Id", "CORE-037-WT-08")]
    public void TopBar_NoProfile_HidesCompanyAndRoleButKeepsLanguagesAndLogout()
    {
        var top = Render<ShellTopBar>();

        top.FindAll("[data-testid=shell-company]").Should().BeEmpty();
        top.FindAll("[data-testid=shell-role]").Should().BeEmpty();
        top.FindAll("[data-testid=shell-language] option").Select(o => o.GetAttribute("value")).Should().Equal(WebLocalization.Cultures);
        top.FindAll("[data-testid=shell-logout]").Should().ContainSingle();
    }
}

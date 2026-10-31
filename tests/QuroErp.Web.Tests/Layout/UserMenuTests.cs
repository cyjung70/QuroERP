using System.Globalization;
using Bunit;
using FluentAssertions;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;

namespace QuroErp.Web.Tests.Layout;

/// <summary>Header user menu (TASK-CORE-023): my info, change password and logout for every signed-in user.</summary>
public class UserMenuTests : BunitContext
{
    public UserMenuTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        Services.AddWebLocalization();
        Services.AddCompanyTime();
    }

    [Fact, Trait("Id", "CORE-023-T01")]
    public void SignedIn_ShowsNameAndMenuItemsWithLinks()
    {
        AddAuthorization().SetAuthorized("kim");

        var menu = Render<UserMenu>();

        menu.Find("[data-testid=user-menu] summary").TextContent.Should().Be("kim");
        menu.Find("[data-testid=user-menu-profile]").GetAttribute("href").Should().Be("/bc/user-profile");
        menu.Find("[data-testid=user-menu-profile]").TextContent.Should().Be("내 정보");
        menu.Find("[data-testid=user-menu-change-password]").GetAttribute("href").Should().Be(WebAuthentication.ChangePasswordPath);
        menu.Find("[data-testid=user-menu-change-password]").TextContent.Should().Be("비밀번호 변경");
        var logout = menu.Find("[data-testid=user-menu-logout]");
        logout.TextContent.Should().Be("로그아웃");
        logout.Closest("form")!.GetAttribute("action").Should().Be(WebAuthentication.LogoutPath);
        logout.Closest("form")!.GetAttribute("method").Should().Be("post");
    }

    [Fact, Trait("Id", "CORE-023-T02")]
    public void SignedInWithoutMenuRights_StillShowsTheMenu()
    {
        AddAuthorization().SetAuthorized("viewer").SetRoles();

        Render<UserMenu>().FindAll("[data-testid=user-menu]").Should().ContainSingle();
    }

    [Fact, Trait("Id", "CORE-023-T03")]
    public void NotSignedIn_ShowsNoMenu()
    {
        AddAuthorization();

        Render<UserMenu>().FindAll("[data-testid=user-menu]").Should().BeEmpty();
    }
}

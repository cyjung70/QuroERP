using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Layout;

/// <summary>
/// Screen tabs (TASK-CORE-061, app-shell.md 3.3), drawn with the screen type samples. The layout passes the new path after each
/// navigation; the tests do the same with <see cref="Open"/>.
/// </summary>
public class ShellTabsTests : ScreenContext
{
    private const string Unsaved = "저장하지 않은 변경 내용이 있습니다. 계속하면 변경 내용이 사라집니다. 계속할까요?";

    private static readonly AuthMenu[] Menus =
    [
        new("G9TS", "Test", null, "bc", null, null, "M"),
        new("G9TS01", "Single sample", "/samples/single", "bc", "G9TS", "Y", "P"),
        new("G9TS02", "Multi sample", "/samples/multi", "bc", "G9TS", "Y", "P"),
        .. Enumerable.Range(1, 11).Select(i =>
            new AuthMenu($"G9TS{i + 10}", $"Order {i}", $"/samples/po-entry/PO{i}", "bc", "G9TS", "Y", "P")),
    ];

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ShellTabs> RenderTabs(string path = "/") =>
        Render<ShellTabs>(p => p.Add(x => x.CurrentPath, path).Add(x => x.Menus, Menus).Add(x => x.HomeUserName, "Kim"));

    // What the layout does after a navigation: the address changes and the host gets the new path
    private static void Open(IRenderedComponent<ShellTabs> tabs, string path) => tabs.Render(p => p.Add(x => x.CurrentPath, path));

    private static List<string?> TabIds(IRenderedComponent<ShellTabs> tabs) =>
        tabs.FindAll("[data-shell-tab]").Select(t => t.GetAttribute("data-testid")).ToList();

    [Fact, Trait("Id", "CORE-061-WT-04")]
    public void SamePath_OpenedTwice_IsOneTab()
    {
        var tabs = RenderTabs();

        Open(tabs, "/samples/single");
        Open(tabs, "/");
        Open(tabs, "/samples/single");

        TabIds(tabs).Should().Equal("shell-tab-home", "shell-tab-g9ts01");
        tabs.Find("[data-testid=shell-tab-g9ts01]").GetAttribute("aria-selected").Should().Be("true");
        tabs.Find("[data-testid=shell-tab-g9ts01] .shell-tab-name").TextContent.Should().Be("Single sample");
        tabs.Find("[data-testid=shell-tab-home] .shell-tab-name").TextContent.Should().Be("홈");
        tabs.FindAll("[data-testid=shell-pane-g9ts01]").Should().ContainSingle();
    }

    [Fact, Trait("Id", "CORE-061-WT-05")]
    public void EleventhTab_IsNotOpened_AndShowsTheMaxMessage()
    {
        var tabs = RenderTabs();
        for (var i = 1; i <= ShellTabs.MaxTabs; i++)
        {
            Open(tabs, $"/samples/po-entry/PO{i}");
        }

        Open(tabs, "/samples/po-entry/PO11");

        TabIds(tabs).Should().HaveCount(ShellTabs.MaxTabs + 1, "the home tab is not counted");
        TabIds(tabs).Should().NotContain("shell-tab-g9ts21");
        tabs.Find("[data-testid=shell-tabs-message]").TextContent.Should().Be("탭은 최대 10개까지 열 수 있습니다. 다른 탭을 닫고 여세요.");
        Navigation.Uri.Should().EndWith("/samples/po-entry/PO10", "the address goes back to the active tab");
    }

    [Fact, Trait("Id", "CORE-061-WT-06")]
    public void SwitchingTabs_KeepsTheTypedValue_AndDoesNotDrawTheScreenAgain()
    {
        var tabs = RenderTabs("/samples/single");
        tabs.Find("[data-testid=shell-pane-g9ts01] [data-testid=single-header-code] input").Change("abc");

        Open(tabs, "/samples/multi");
        tabs.Find("[data-testid=shell-pane-g9ts01]").HasAttribute("hidden").Should().BeTrue();
        tabs.Find("[data-testid=shell-pane-g9ts02]").HasAttribute("hidden").Should().BeFalse();
        Open(tabs, "/samples/single");

        tabs.Find("[data-testid=shell-pane-g9ts01]").HasAttribute("hidden").Should().BeFalse();
        tabs.Find("[data-testid=shell-pane-g9ts01] [data-testid=single-header-code] input").GetAttribute("value").Should().Be("ABC");
    }

    [Fact, Trait("Id", "CORE-061-WT-07")]
    public void ClosingATabWithChanges_AsksFirst_AndShowsTheDot()
    {
        var tabs = RenderTabs("/samples/single");
        tabs.Find("[data-testid=single-header-name] input").Change("Cherry");
        tabs.WaitForAssertion(() => tabs.Find("[data-testid=shell-tab-g9ts01]").GetAttribute("data-dirty").Should().Be("true"));
        tabs.Find("[data-testid=shell-tab-g9ts01] .shell-tab-dirty").TextContent.Should().Be("●");

        Confirm.Answer = false;
        tabs.Find("[data-testid=shell-tab-close-g9ts01]").Click();
        Confirm.Questions.Should().Equal(Unsaved);
        TabIds(tabs).Should().Contain("shell-tab-g9ts01", "the user kept the changes");

        Confirm.Answer = true;
        tabs.Find("[data-testid=shell-tab-close-g9ts01]").Click();
        TabIds(tabs).Should().Equal("shell-tab-home");
        Navigation.Uri.Should().Be("http://localhost/", "no tab on the right, so the home tab on the left");
    }

    [Fact, Trait("Id", "CORE-061-WT-08")]
    public void HomeTab_IsFirst_AndCannotBeClosed()
    {
        var tabs = RenderTabs();
        Open(tabs, "/samples/single");

        TabIds(tabs)[0].Should().Be("shell-tab-home");
        tabs.FindAll("[data-testid=shell-tab-close-home]").Should().BeEmpty();
        tabs.Find("[data-testid=shell-tab-home]").MouseUp(new MouseEventArgs { Button = 1 });
        tabs.Find("[data-testid=shell-tab-g9ts01]").ContextMenu();
        tabs.Find("[data-testid=shell-tab-menu-close-all]").Click();

        TabIds(tabs).Should().Equal("shell-tab-home");
        tabs.Find("[data-testid=shell-pane-home]").TextContent.Should().Contain("Kim");
    }

    [Fact, Trait("Id", "CORE-061-WT-09")]
    public void MiddleButton_ClosesATab_AndGoesToTheTabOnTheRight()
    {
        var tabs = RenderTabs();
        Open(tabs, "/samples/single");
        Open(tabs, "/samples/multi");
        Open(tabs, "/samples/single");

        tabs.Find("[data-testid=shell-tab-g9ts01]").MouseUp(new MouseEventArgs { Button = 1 });

        TabIds(tabs).Should().Equal("shell-tab-home", "shell-tab-g9ts02");
        Navigation.Uri.Should().EndWith("/samples/multi");
    }

    [Fact, Trait("Id", "CORE-061-WT-10")]
    public void CloseOthers_KeepsThatTabAndTheHomeTab()
    {
        var tabs = RenderTabs();
        Open(tabs, "/samples/single");
        Open(tabs, "/samples/multi");
        Open(tabs, "/samples/po-entry/PO1");

        tabs.Find("[data-testid=shell-tab-g9ts02]").ContextMenu();
        tabs.Find("[data-testid=shell-tab-menu-close-others]").Click();

        TabIds(tabs).Should().Equal("shell-tab-home", "shell-tab-g9ts02");
        Navigation.Uri.Should().EndWith("/samples/multi");
    }

    [Fact, Trait("Id", "CORE-061-WT-11")]
    public void Reload_RestoresTheTabPaths_ButDrawsAScreenOnlyWhenItsTabOpens()
    {
        JSInterop.Setup<string[]>("quroShellTabs.load").SetResult(["/samples/multi", "/no/such/screen", "/samples/single"]);

        var tabs = RenderTabs("/samples/single");

        tabs.WaitForAssertion(() => TabIds(tabs).Should().Equal("shell-tab-home", "shell-tab-g9ts02", "shell-tab-g9ts01"));
        tabs.FindAll("[data-testid=shell-pane-g9ts02]").Should().BeEmpty("a restored screen opens empty when picked");
        tabs.Find("[data-testid=shell-tab-g9ts01]").GetAttribute("aria-selected").Should().Be("true");
        JSInterop.Invocations["quroShellTabs.save"][^1].Arguments.Should().ContainSingle("the paths go as one array argument")
            .Which.Should().BeOfType<string[]>().Which.Should().Equal("/samples/multi", "/samples/single");
    }

    [Fact, Trait("Id", "CORE-061-WT-12")]
    public void TabList_ListsTheOpenTabs()
    {
        var tabs = RenderTabs();
        Open(tabs, "/samples/single");
        Open(tabs, "/samples/multi");

        tabs.Find("[data-testid=shell-tabs-list]").Click();

        tabs.FindAll("[data-testid=shell-tabs-list-menu] button").Select(b => b.TextContent.Trim())
            .Should().Equal("홈", "Single sample", "Multi sample");
        tabs.Find("[data-testid=shell-tabs-list]").GetAttribute("aria-label").Should().Be("열린 탭 목록");
    }
}

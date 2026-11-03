using System.Globalization;
using Bunit;
using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages;

namespace QuroErp.Web.Tests.Common;

/// <summary>Home phase 1: process flows and shortcuts with their state (TASK-CORE-038, app-shell.md 6).</summary>
public class HomeTests : BunitContext
{
    private static readonly AuthMenu[] AdminMenus =
    [
        new("G1BA", "Master Data", null, "bc", null, null, "M"),
        new("G1BA01", "Items", "/bc/item", "bc", "G1BA", "Y", "P"),
        new("G5AD", "Administration", null, "bc", null, null, "M"),
        new("G5AD01", "Company", "/bc/company", "bc", "G5AD", "Y", "P"),
    ];

    private static readonly string[] AllStepIds =
    [
        "home-step-purchase-order", "home-step-goods-receipt", "home-step-supplier-invoice", "home-step-ap-payment",
        "home-step-sales-order", "home-step-shipment", "home-step-sales-invoice", "home-step-ar-receipt",
        "home-link-item", "home-link-customer", "home-link-vendor", "home-link-stock", "home-link-company",
    ];

    public HomeTests()
    {
        UseCulture("ko");
        Services.AddWebLocalization();
        Services.AddCompanyTime();
    }

    private static void UseCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);

    private IRenderedComponent<HomeBoard> RenderBoard(IReadOnlyList<AuthMenu> menus, string? lastLogin = null) =>
        Render<HomeBoard>(p => p.Add(x => x.UserName, "Kim").Add(x => x.LastLogin, lastLogin).Add(x => x.Menus, menus));

    [Fact, Trait("Id", "CORE-038-UT-01")]
    public void StateOf_ScreenNotBuilt_IsNotReady_EvenWithAMenu()
    {
        var step = new HomeStep("purchase-order", "/mm/purchase-order", "Home.Step.PurchaseOrder", false);
        AuthMenu[] menus = [new("G2MM01", "PO", "/mm/purchase-order", "mm", "G2MM", "Y", "P")];

        HomeSteps.StateOf(step, menus).Should().Be(HomeStepState.NotReady);
    }

    [Theory, Trait("Id", "CORE-038-UT-02")]
    [InlineData("/bc/item", "P", HomeStepState.Ready)]
    [InlineData("/BC/Item/", "P", HomeStepState.Ready)]
    [InlineData("/bc/item", "U", HomeStepState.NoPermission)]
    [InlineData("/bc/item-price", "P", HomeStepState.NoPermission)]
    public void StateOf_BuiltScreen_IsReadyOnlyWithAProgramMenuForItsPath(string programId, string type, HomeStepState expected)
    {
        var step = new HomeStep("item", "/bc/item", "Home.Link.Item", true);
        AuthMenu[] menus = [new("G1BA01", "Items", programId, "bc", "G1BA", "Y", type)];

        HomeSteps.StateOf(step, menus).Should().Be(expected);
    }

    [Fact, Trait("Id", "CORE-038-UT-03")]
    public void Steps_FollowTheSpecTable_WithItemAndCompanyBuilt()
    {
        HomeSteps.PurchaseFlow.Select(s => s.Path).Should().Equal("/mm/purchase-order", "/mm/goods-receipt", "/mm/supplier-invoice", "/mm/ap-payment");
        HomeSteps.SalesFlow.Select(s => s.Path).Should().Equal("/sd/sales-order", "/sd/shipment", "/sd/sales-invoice", "/sd/ar-receipt");
        HomeSteps.MasterLinks.Select(s => s.Path).Should().Equal("/bc/item", "/sd/customer", "/mm/vendor", "/mm/report-item-list");
        HomeSteps.SettingsLinks.Select(s => s.Path).Should().Equal("/bc/company");
        HomeSteps.PurchaseFlow.Concat(HomeSteps.SalesFlow).Concat(HomeSteps.MasterLinks).Concat(HomeSteps.SettingsLinks)
            .Where(s => s.ScreenReady).Select(s => s.Id).Should().BeEquivalentTo("item", "company");
    }

    [Fact, Trait("Id", "CORE-038-WT-01")]
    public void Board_ShowsWelcomeLastLoginSectionsAndEveryStep()
    {
        var home = RenderBoard(AdminMenus, "직전 로그인: 2026-10-30 9:00 (IP 10.0.0.7)");

        home.Find("[data-testid=home-welcome] h1").TextContent.Should().Be("Kim님, 환영합니다.");
        home.Find("[data-testid=home-last-login]").TextContent.Should().Be("직전 로그인: 2026-10-30 9:00 (IP 10.0.0.7)");
        foreach (var section in new[] { "home-flow-purchase", "home-flow-sales", "home-links-master", "home-links-settings" })
        {
            home.FindAll($"[data-testid={section}]").Should().ContainSingle(section);
        }
        home.FindAll("[data-testid^=home-step-], [data-testid^=home-link-]").Select(e => e.GetAttribute("data-testid"))
            .Should().Equal(AllStepIds);
        home.Find("[data-testid=home-flow-purchase] h2").TextContent.Should().Be("구매 흐름");
    }

    [Fact, Trait("Id", "CORE-038-WT-02")]
    public void Board_ReadyStep_IsALinkToItsPath()
    {
        var home = RenderBoard(AdminMenus);

        var item = home.Find("[data-testid=home-link-item]");
        item.TagName.Should().Be("A");
        item.GetAttribute("href").Should().Be("/bc/item");
        item.GetAttribute("data-state").Should().Be("ready");
        item.TextContent.Should().Contain("품목");
        home.Find("[data-testid=home-link-company]").GetAttribute("href").Should().Be("/bc/company");
    }

    [Fact, Trait("Id", "CORE-038-WT-03")]
    public void Board_StepWithoutPermission_IsDisabledWithTooltip()
    {
        var home = RenderBoard([AdminMenus[2], AdminMenus[3]]);

        var item = home.Find("[data-testid=home-link-item]");
        item.TagName.Should().NotBe("A");
        item.HasAttribute("href").Should().BeFalse();
        item.GetAttribute("data-state").Should().Be("no-permission");
        item.GetAttribute("aria-disabled").Should().Be("true");
        item.GetAttribute("title").Should().Be("이 화면을 열 권한이 없습니다.");
        item.QuerySelector(".home-step-badge").Should().BeNull();
    }

    [Fact, Trait("Id", "CORE-038-WT-04")]
    public void Board_StepNotBuilt_ShowsThePhase2Badge_AndNoLink()
    {
        var home = RenderBoard(AdminMenus);

        var order = home.Find("[data-testid=home-step-purchase-order]");
        order.TagName.Should().NotBe("A");
        order.GetAttribute("data-state").Should().Be("not-ready");
        order.GetAttribute("aria-disabled").Should().Be("true");
        order.QuerySelector(".home-step-badge")!.TextContent.Should().Be("2단계");
        home.FindAll("a.home-step").Select(a => a.GetAttribute("data-testid")).Should().Equal("home-link-item", "home-link-company");
    }

    [Fact, Trait("Id", "CORE-038-WT-05")]
    public void Board_English_UsesEnglishResources_AndNoAmountsOrCounts()
    {
        UseCulture("en");

        var home = RenderBoard(AdminMenus);

        home.Find("[data-testid=home-welcome] h1").TextContent.Should().Be("Welcome, Kim.");
        home.Find("[data-testid=home-step-sales-invoice] .home-step-name").TextContent.Should().Be("Sales Invoice");
        home.Find("[data-testid=home-step-sales-invoice] .home-step-badge").TextContent.Should().Be("Phase 2");
        home.Find("[data-testid=home-links-settings] h2").TextContent.Should().Be("Settings");
        home.FindAll("[data-testid=home-last-login]").Should().BeEmpty("there is no previous sign-in");
        // Phase 1 shows no counts or amounts: a step holds only its icon, name and badge
        home.FindAll(".home-step > *").Select(e => e.ClassName).Distinct()
            .Should().BeSubsetOf(["home-step-icon", "home-step-name", "home-step-badge"]);
    }
}

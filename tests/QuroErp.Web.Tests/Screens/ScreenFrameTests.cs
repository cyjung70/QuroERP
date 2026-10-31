using System.Globalization;
using Bunit;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Common frame of the S, M and MM screens (TASK-CORE-040, screen-templates.md 0).</summary>
public class ScreenFrameTests : ScreenContext
{
    private static readonly string[] Regions = ["title", "message", "toolbar", "search", "header", "grid", "line", "audit"];

    // Frame regions of the screen in document order
    private static IEnumerable<string> RegionOrder<T>(IRenderedComponent<T> page, string screen) where T : Microsoft.AspNetCore.Components.IComponent =>
        page.FindAll("[data-testid]").Select(e => e.GetAttribute("data-testid")!)
            .Where(id => Regions.Any(r => id == $"{screen}-{r}"))
            .Select(id => id[(screen.Length + 1)..]);

    private static void SearchSingle(IRenderedComponent<SampleSingle> page, string code)
    {
        page.Find("[data-testid=single-search-code]").Change(code);
        page.Find("[data-testid=single-toolbar-search]").Click();
    }

    [Fact, Trait("Id", "CORE-040-WT-01")]
    public void Frames_ShowTheirRegionsInTheTemplateOrder()
    {
        var single = Render<SampleSingle>();
        var multi = Render<SampleMulti>();
        var masterDetail = Render<SampleMasterDetail>();

        RegionOrder(single, "single").Should().Equal("title", "toolbar", "search", "header", "audit");
        RegionOrder(multi, "multi").Should().Equal("title", "toolbar", "grid");
        RegionOrder(masterDetail, "master-detail").Should().Equal("title", "toolbar", "grid");
        single.Find("[data-testid=single-title] .quro-entry-path").TextContent.Should().Be("샘플 › 샘플: 단일 (S)");
        multi.Find("[data-testid=multi-title] h1").TextContent.Should().Be("샘플: 다중 (M)");
        // M and MM have no record code, chip or audit
        multi.FindAll("[data-testid=multi-doc-no], [data-testid=multi-status-chip], [data-testid=multi-audit]").Should().BeEmpty();
        masterDetail.FindAll("[data-testid=master-detail-doc-no], [data-testid=master-detail-audit]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-040-WT-02")]
    public void CommandBar_SaveIsBrand_DeleteIsDanger_ExtraCommandsAfterASeparator()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-toolbar-save]").ClassList.Should().Contain("quro-primary");
        page.Find("[data-testid=single-toolbar-delete]").ClassList.Should().Contain("quro-danger-text");
        page.FindAll("[data-testid=single-toolbar] .quro-primary").Should().ContainSingle("only save is filled");
        var extra = page.Find("[data-testid=single-toolbar] [data-group=extra]");
        extra.PreviousElementSibling!.ClassList.Should().Contain("quro-commandbar-sep");
        extra.QuerySelectorAll("button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("single-toolbar-deactivate", "single-toolbar-activate");
    }

    [Fact, Trait("Id", "CORE-040-WT-03")]
    public void BlockedButtons_AreDisabled_NotHidden()
    {
        var single = Render<SampleSingle>();
        var masterDetail = Render<SampleMasterDetail>();

        // A new record: nothing to delete, and the record commands do not apply yet
        single.Find("[data-testid=single-toolbar-delete]").HasAttribute("disabled").Should().BeTrue();
        single.Find("[data-testid=single-toolbar-deactivate]").HasAttribute("disabled").Should().BeTrue();
        // MM before a source row is picked
        masterDetail.Find("[data-testid=master-detail-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        masterDetail.Find("[data-testid=master-detail-toolbar-save]").HasAttribute("disabled").Should().BeTrue();

        masterDetail.FindAll("[data-testid=master-detail-grid-select]")[0].Click();

        masterDetail.Find("[data-testid=master-detail-toolbar-new]").HasAttribute("disabled").Should().BeFalse();
        masterDetail.Find("[data-testid=master-detail-toolbar-save]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-040-WT-04")]
    public void Single_TitleShowsCodeAndChip_AuditIsEmptyForANewRecord()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=single-status-chip]").Should().BeEmpty();
        page.Find("[data-testid=single-audit]").Children.Should().BeEmpty();

        SearchSingle(page, "A100");
        page.Find("[data-testid=single-doc-no]").TextContent.Should().Be("A100");
        page.Find("[data-testid=single-status-chip]").TextContent.Should().Be("사용");

        page.Find("[data-testid=single-toolbar-deactivate]").Click();

        page.Find("[data-testid=single-status-chip]").TextContent.Should().Be("사용 중지");
        page.Find("[data-testid=single-toolbar-deactivate]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=single-audit]").TextContent.Should().Contain("수정").And.Contain("sample");
        Store.FindItem("A100")!.Active.Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-040-WT-05")]
    public void Frame_English_UsesEnglishResources()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-title] .quro-entry-path").TextContent.Should().Be("Samples › Sample: Single (S)");
        page.Find("[data-testid=single-doc-no]").TextContent.Should().Be("New document");
        page.Find("[data-testid=single-toolbar-deactivate]").TextContent.Should().Be("Deactivate");
        SearchSingle(page, "A100");
        page.Find("[data-testid=single-status-chip]").TextContent.Should().Be("Active");
    }
}

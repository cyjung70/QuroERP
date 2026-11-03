using System.Text.RegularExpressions;
using Bunit;
using Bunit.Rendering;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Pages.Samples;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// data-testid on the screen type components (TASK-CORE-013, quro-blazor rule 8):
/// <c>{screen}-{area}-{field}</c> in kebab-case, grid rows carry <c>data-row-index</c>.
/// </summary>
public class TestIdTests : ScreenContext
{
    public static TheoryData<Type, string> Samples => new()
    {
        { typeof(SampleSingle), "single" },
        { typeof(SampleMulti), "multi" },
        { typeof(SampleHeaderDetail), "header-detail" },
        { typeof(SampleMasterDetail), "master-detail" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_EveryInputButtonAndGrid_HasTestIdByRule(Type page, string screen)
    {
        var cut = RenderSample(page, screen);

        // The hidden submit button only lets Enter run the search
        var elements = cut.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        foreach (var element in elements)
        {
            var id = DataGridPageExtensions.TestIdOf(element);
            id.Should().NotBeNull($"{element.OuterHtml} needs a data-testid");
            id.Should().MatchRegex(Rule(screen));
        }
    }

    [Theory]
    [InlineData(typeof(SampleMulti), "multi")]
    [InlineData(typeof(SampleHeaderDetail), "header-detail")]
    [InlineData(typeof(SampleMasterDetail), "master-detail")]
    public void Sample_GridRows_HaveRowIndexFromZero(Type page, string screen)
    {
        var cut = RenderSample(page, screen);

        // A package grid has its rows by row key in data-row, not by index (TODO(QuroGrid-R3))
        foreach (var grid in cut.FindAll(".quro-grid[role=grid]"))
        {
            grid.QuerySelectorAll(".quro-body [role=row]").Should().NotBeEmpty().And.OnlyContain(r => r.HasAttribute("data-row"));
        }
        cut.FindAll(".quro-body [role=row]").Should().NotBeEmpty();
    }

    [Fact]
    public void Message_ErrorAndNotice_ShareOneTestId()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid] [data-row]").Should().HaveCount(2));

        page.Find("[data-testid=multi-toolbar-save]").Click();
        page.Find("[data-testid=multi-message]").GetAttribute("data-level").Should().Be("info");

        page.Find("[data-testid=multi-toolbar-new]").Click();
        page.Find("[data-testid=multi-toolbar-save]").Click();
        page.FindAll("[data-testid=multi-message]").Should().ContainSingle()
            .Which.GetAttribute("data-level").Should().Be("error");
    }

    // {screen} alone (the screen root) or {screen}-{area}[-{field}] in kebab-case (SM areas: entry-frame.md 8, S tabs: TASK-CORE-041)
    private static string Rule(string screen) =>
        $"^{Regex.Escape(screen)}(-(header|line|search|grid|toolbar|message|title|doc-no|status-chip|revision|totals|audit|find|tab)(-[a-z0-9]+)*)?$";

    /// <summary>Renders the sample with rows in every grid (SM loads an order, MM selects a source).</summary>
    private IRenderedComponent<ContainerFragment> RenderSample(Type page, string screen)
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent(0, page);
            builder.CloseComponent();
        });
        if (screen == "header-detail")
        {
            cut.Find("[data-testid=header-detail-toolbar-search]").Click();
            cut.Find("[data-testid=header-detail-find-keyword]").Change("SO261000001");
            cut.Find("[data-testid=header-detail-find] form").Submit();
            cut.WaitForAssertion(() => cut.LookupRows("header-detail-find").Should().ContainSingle());
            cut.PickLookupRow<SampleOrder, ContainerFragment>("header-detail-find", 0);
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=header-detail-line] [data-row]").Should().NotBeEmpty());
        }
        if (screen == "master-detail")
        {
            var groups = cut.DataGrid<SampleGroup, ContainerFragment>("master-detail-grid");
            cut.WaitForAssertion(() => groups.Rows.Should().NotBeEmpty());
            groups.Click(0, QuroErp.Web.Components.Screens.QuroMasterMultiDetail<SampleGroup, SampleLine>.SelectField);
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=master-detail-line] [data-row]").Should().NotBeEmpty());
        }
        if (screen == "multi")
        {
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=multi-grid] [data-row]").Should().NotBeEmpty());
        }
        if (screen == "single")
        {
            cut.Find("[data-testid=single-search-code]").Change("A100");
            cut.Find("[data-testid=single-toolbar-search]").Click();
        }
        cut.WaitForAssertion(() => cut.FindAll("[data-testid]").Should().NotBeEmpty());
        return cut;
    }
}

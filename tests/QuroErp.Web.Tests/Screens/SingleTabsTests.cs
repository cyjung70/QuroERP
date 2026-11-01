using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// S tabs, read-only sub-grids and the screen's own commands on the item-layout sample (TASK-CORE-041,
/// screen-templates.md 1).
/// </summary>
public class SingleTabsTests : ScreenContext
{
    private const string Unsaved = "저장하지 않은 변경 내용이 있습니다. 계속하면 변경 내용이 사라집니다. 계속할까요?";

    private static void Search(IRenderedComponent<SampleSingle> page, string code)
    {
        page.Find("[data-testid=single-search-code]").Change(code);
        page.Find("[data-testid=single-toolbar-search]").Click();
    }

    // A Quro.UI control has the test id on its field, a plain input on itself (TODO(QuroGrid-R3))
    private static string? Value(IRenderedComponent<SampleSingle> page, string testId) =>
        page.Find($"[data-testid={testId}] input, input[data-testid={testId}]").GetAttribute("value");

    [Fact, Trait("Id", "CORE-041-WT-01")]
    public void Tabs_SwitchingKeepsTheChange_AndSaveStoresItOnce()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");
        page.Find("[data-testid=single-header-name] input").Change("Apricot");

        page.Find("[data-testid=single-tab-stock]").Click();
        page.Find("[data-testid=single-header]").HasAttribute("hidden").Should().BeTrue();
        page.Find("[data-testid=single-tab-fields]").Click();

        page.Find("[data-testid=single-header]").HasAttribute("hidden").Should().BeFalse();
        Value(page, "single-header-name").Should().Be("Apricot");
        Confirm.Answer = false;
        page.Find("[data-testid=single-toolbar-search]").Click();
        Confirm.Questions.Should().Equal(Unsaved);

        page.Find("[data-testid=single-tab-transactions]").Click();
        page.Find("[data-testid=single-toolbar-save]").Click();
        Store.FindItem("A100")!.Name.Should().Be("Apricot");
        Store.Items().Should().HaveCount(2);
    }

    [Fact, Trait("Id", "CORE-041-WT-02")]
    public void SubGrid_ReadsWhenItsTabFirstOpens_ShowsTotalAndNote_AndIsReadOnly()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");
        Store.StockReads.Should().Be(0);

        page.Find("[data-testid=single-tab-stock]").Click();

        var stock = page.DataGrid<SampleStock, SampleSingle>("single-grid-stock");
        page.WaitForAssertion(() => stock.Rows.Should().HaveCount(2));
        var grid = page.Find("[data-testid=single-grid-stock]");
        grid.QuerySelectorAll(".quro-head [role=columnheader][data-field]").Should().HaveCount(4, "no row state or delete column");
        grid.QuerySelectorAll(".quro-body [role=gridcell]").Should().OnlyContain(c => c.GetAttribute("aria-readonly") == "true");
        grid.QuerySelectorAll("[data-footer-field]").Skip(1).Select(c => c.TextContent.Trim())
            .Should().Equal("15.00", "2.00", "13.00");
        page.Find("[data-testid=single-grid-stock-note]").TextContent.Should().Be("가용 = 현재고 - 할당.");

        page.Find("[data-testid=single-tab-fields]").Click();
        page.Find("[data-testid=single-tab-stock]").Click();
        Store.StockReads.Should().Be(1, "the tab already read this record");

        // Another record while the tab is open reads again
        Search(page, "B200");
        page.WaitForAssertion(() => stock.Rows.Should().ContainSingle());
        Store.StockReads.Should().Be(2);
    }

    [Fact, Trait("Id", "CORE-041-WT-03")]
    public void SubGrid_OfANewRecord_ShowsNoData_WithoutReading()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-tab-stock]").Click();

        page.FindAll("[data-testid=single-grid-stock]").Should().BeEmpty();
        page.Find("[role=tabpanel]:not([hidden]) .quro-no-data").TextContent.Should().Be("조회된 내용이 없습니다.");
        Store.StockReads.Should().Be(0);
    }

    [Fact, Trait("Id", "CORE-041-WT-04")]
    public void Transactions_OpenButtonOfARow_OpensItsDocument()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");
        page.Find("[data-testid=single-tab-transactions]").Click();
        var transactions = page.DataGrid<SampleTransaction, SampleSingle>("single-grid-transactions");
        page.WaitForAssertion(() => transactions.Rows.Should().HaveCount(2));

        // The package grid has no row double-click: each row has an open button (TASK-CORE-056)
        transactions.Click(0, QuroListGrid<SampleTransaction>.OpenField);

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/samples/po-entry/PO261000001");
    }

    [Fact, Trait("Id", "CORE-041-WT-05")]
    public void CopyAsNew_ClearsTheCode_KeepsTheValues_CountsAsChanged_AndSavesASecondRecord()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");

        page.Find("[data-testid=single-toolbar-copy]").Click();

        page.Find("[data-testid=single-doc-no]").ClassList.Should().Contain("quro-entry-new");
        Value(page, "single-header-code").Should().BeNullOrEmpty();
        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeFalse();
        Value(page, "single-header-name").Should().Be("Apple");
        page.Find("[data-testid=single-audit]").TextContent.Trim().Should().BeEmpty();
        Confirm.Answer = false;
        page.Find("[data-testid=single-toolbar-search]").Click();
        Confirm.Questions.Should().Equal(Unsaved);

        page.Find("[data-testid=single-header-code] input").Change("C300");
        page.Find("[data-testid=single-toolbar-save]").Click();
        Store.FindItem("C300")!.Name.Should().Be("Apple");
        Store.FindItem("A100").Should().NotBeNull();
    }

    [Fact, Trait("Id", "CORE-041-WT-06")]
    public void Deactivate_AsksFirst_AndNoChangesNothing()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");
        Confirm.Answer = false;

        page.Find("[data-testid=single-toolbar-deactivate]").Click();

        Confirm.Questions.Should().Equal("이 품목을 사용 중지할까요?");
        Store.FindItem("A100")!.Active.Should().BeTrue();
        Confirm.Answer = true;
        page.Find("[data-testid=single-toolbar-deactivate]").Click();
        Store.FindItem("A100")!.Active.Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-041-WT-07")]
    public void Fields_AreA4ColumnGrid_WithSpans_AndRadioCards()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-header]").ClassList.Should().Contain("quro-form");
        page.Find("[data-testid=single-header-name] input").Closest(".quro-field")!.ClassList.Should().Contain("quro-span-2");
        page.Find("[data-testid=single-header-remarks]").Closest(".quro-field")!.ClassList.Should().Contain("quro-span-4");
        page.Find("[data-testid=single-header-supply-p]").ParentElement!.ClassList.Should().Contain("quro-radio-card-selected");

        page.Find("[data-testid=single-header-supply-m]").Change("M");

        page.Find("[data-testid=single-header-supply-m]").ParentElement!.ClassList.Should().Contain("quro-radio-card-selected");
        page.Find("[data-testid=single-header-supply-p]").ParentElement!.ClassList
            .Should().Contain("quro-radio-card").And.NotContain("quro-radio-card-selected");
    }
}

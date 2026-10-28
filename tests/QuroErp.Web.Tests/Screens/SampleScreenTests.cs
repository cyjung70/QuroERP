using Bunit;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// The four screen type samples (TASK-CORE-006 "4개 유형 샘플 화면이 동작한다",
/// "저장하지 않은 변경 확인, 행 상태(I/U/D)가 동작한다").
/// </summary>
public class SampleScreenTests : ScreenContext
{
    private const string Unsaved = "저장하지 않은 변경 내용이 있습니다. 계속하면 변경 내용이 사라집니다. 계속할까요?";

    // S

    [Fact]
    public void Single_ToolbarButtons_InFixedOrder()
    {
        var page = Render<SampleSingle>();

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("single-toolbar-search", "single-toolbar-new", "single-toolbar-save", "single-toolbar-delete");
    }

    [Fact]
    public void Single_Search_LoadsRecord_AndLocksNewOnlyField()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code]").HasAttribute("readonly").Should().BeFalse();

        page.Find("[data-testid=single-search-code]").Change("a100");
        page.Find("[data-testid=single-toolbar-search]").Click();

        page.Find("[data-testid=single-header-name]").GetAttribute("value").Should().Be("Apple");
        page.Find("[data-testid=single-header-code]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=single-toolbar-delete]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Single_TextCaseUpper_ConvertsInput()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-header-code]").Change("abc");

        page.Find("[data-testid=single-header-code]").GetAttribute("value").Should().Be("ABC");
    }

    [Fact]
    public void Single_SaveWithEmptyRequired_ShowsRequiredMessage_AndDoesNotSave()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code]").Change("C300");

        page.Find("[data-testid=single-toolbar-save]").Click();

        page.Find("[data-testid=single-message]").TextContent.Should().Contain("이름을(를) 입력하세요.");
        Store.FindItem("C300").Should().BeNull();
    }

    [Fact]
    public void Single_SaveNew_StoresRecord()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code]").Change("c300");
        page.Find("[data-testid=single-header-name]").Change("Cherry");

        page.Find("[data-testid=single-toolbar-save]").Click();

        Store.FindItem("C300")!.Name.Should().Be("Cherry");
        page.Find("[data-testid=single-message]").TextContent.Should().Contain("저장했습니다.");
        page.Find("[data-testid=single-header-code]").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void Single_NewWithUnsavedChanges_AsksAndKeepsInputWhenCancelled()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-name]").Change("Draft");
        Confirm.Answer = false;

        page.Find("[data-testid=single-toolbar-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.Find("[data-testid=single-header-name]").GetAttribute("value").Should().Be("Draft");
    }

    [Fact]
    public void Single_NewWithoutChanges_DoesNotAsk()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-toolbar-new]").Click();

        Confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public void Single_Delete_AsksThenDeletes()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-search-code]").Change("A100");
        page.Find("[data-testid=single-toolbar-search]").Click();

        page.Find("[data-testid=single-toolbar-delete]").Click();

        Confirm.Questions.Should().Equal("삭제할까요?");
        Store.FindItem("A100").Should().BeNull();
    }

    // M

    [Fact]
    public void Multi_LoadsRows_WithToolbarInFixedOrder()
    {
        var page = Render<SampleMulti>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("multi-toolbar-search", "multi-toolbar-new", "multi-toolbar-save", "multi-toolbar-export");
    }

    [Fact]
    public void Multi_RowStates_AreShownAsIUD()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));

        page.FindAll("[data-testid=multi-grid-name]")[0].Change("Apricot");
        page.FindAll("[data-testid=multi-grid-delete-row]")[1].Click();
        page.Find("[data-testid=multi-toolbar-new]").Click();

        page.FindAll("[data-state]").Select(s => s.GetAttribute("data-state")).Should().Equal("U", "D", "I");
    }

    [Fact]
    public void Multi_Save_SendsBatch_AndReloads()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));
        page.FindAll("[data-testid=multi-grid-name]")[0].Change("Apricot");
        page.FindAll("[data-testid=multi-grid-delete-row]")[1].Click();
        page.Find("[data-testid=multi-toolbar-new]").Click();
        page.FindAll("[data-testid=multi-grid-code]")[2].Change("c300");
        page.FindAll("[data-testid=multi-grid-name]")[2].Change("Cherry");

        page.Find("[data-testid=multi-toolbar-save]").Click();

        Store.Items().Select(i => (i.Code, i.Name)).Should().Equal(("A100", "Apricot"), ("C300", "Cherry"));
        page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2);
        page.FindAll("[data-state]").Should().BeEmpty();
    }

    [Fact]
    public void Multi_SaveWithEmptyRequiredCell_ShowsRequiredMessage()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));
        page.Find("[data-testid=multi-toolbar-new]").Click();

        page.Find("[data-testid=multi-toolbar-save]").Click();

        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("을(를) 입력하세요.");
        Store.Items().Should().HaveCount(2);
    }

    [Fact]
    public void Multi_SaveWithoutChanges_SaysNoChanges()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));

        page.Find("[data-testid=multi-toolbar-save]").Click();

        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("저장할 변경 내용이 없습니다.");
    }

    [Fact]
    public void Multi_SearchWithUnsavedChanges_Asks()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=multi-grid-name]").Should().HaveCount(2));
        page.FindAll("[data-testid=multi-grid-name]")[0].Change("Apricot");
        Confirm.Answer = false;

        page.Find("[data-testid=multi-toolbar-search]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.Find("[data-state=U]").Should().NotBeNull();
    }

    // SM

    [Fact]
    public void HeaderDetail_SaveHeaderAndLines_InOneSave()
    {
        var page = Render<SampleHeaderDetail>();
        page.Find("[data-testid=header-detail-search-order-no]").Change("SO261000001");
        page.Find("[data-testid=header-detail-toolbar-search]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=header-detail-line-item-code]").Should().HaveCount(2));

        page.FindAll("[data-testid=header-detail-line-delete-row]")[0].Click();
        page.Find("[data-testid=header-detail-line-add-row]").Click();
        page.FindAll("[data-testid=header-detail-line-item-code]")[2].Change("c300");
        page.Find("[data-testid=header-detail-toolbar-save]").Click();

        Store.FindOrder("SO261000001")!.Value.Lines.Select(l => l.ItemCode).Should().Equal("B200", "C300");
        page.FindAll("[data-testid=header-detail-line-item-code]").Should().HaveCount(2);
    }

    [Fact]
    public void HeaderDetail_LineChangeOnly_IsUnsavedChange()
    {
        var page = Render<SampleHeaderDetail>();
        page.Find("[data-testid=header-detail-search-order-no]").Change("SO261000001");
        page.Find("[data-testid=header-detail-toolbar-search]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=header-detail-line-item-code]").Should().HaveCount(2));
        page.FindAll("[data-testid=header-detail-line-qty]")[0].Change("9");
        Confirm.Answer = false;

        page.Find("[data-testid=header-detail-toolbar-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
    }

    // SM example: PO Entry

    private static IRenderedComponent<SamplePoEntry> SearchPo(IRenderedComponent<SamplePoEntry> page)
    {
        page.Find("[data-testid=po-entry-search-po-no]").Change("PO261000001");
        page.Find("[data-testid=po-entry-toolbar-search]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=po-entry-line-item-code]").Should().HaveCount(2));
        return page;
    }

    private static string? Value(IRenderedComponent<SamplePoEntry> page, string testId, int index = 0) =>
        page.FindAll($"[data-testid={testId}]")[index].GetAttribute("value");

    [Fact]
    public void PoEntry_Search_LoadsHeaderLinesAndTotal()
    {
        var page = SearchPo(Render<SamplePoEntry>());

        Value(page, "po-entry-header-vendor-name").Should().Be("ABC Trading Co.");
        Value(page, "po-entry-header-po-no").Should().Be("PO261000001");
        page.Find("[data-testid=po-entry-header-po-no]").HasAttribute("readonly").Should().BeTrue();
        Value(page, "po-entry-summary-total-amt").Should().Be("1,825.00");
    }

    [Fact]
    public void PoEntry_TypedItemCode_FillsNameAndDefaultPrice_AndUpdatesTotal()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-line-add-row]").Click();

        page.Find("[data-testid=po-entry-line-item-code]").Change("kbd10001");
        page.Find("[data-testid=po-entry-line-qty]").Change("4");

        page.Find("tr[data-row-index='0']").TextContent.Should().Contain("Keyboard USB");
        Value(page, "po-entry-line-fob").Should().Be("12.50");
        Value(page, "po-entry-summary-total-amt").Should().Be("50.00");
    }

    [Fact]
    public void PoEntry_ItemLookup_SelectFillsLine_AndCloses()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-line-add-row]").Click();

        page.Find("[data-testid=po-entry-line-item-lookup]").Click();
        page.FindAll("[data-testid=po-entry-item-lookup-grid-select]").Should().HaveCount(4);
        page.FindAll("[data-testid=po-entry-item-lookup-grid-select]")[2].Click();

        Value(page, "po-entry-line-item-code").Should().Be("MON24001");
        Value(page, "po-entry-line-fob").Should().Be("180.00");
        page.FindAll("[data-testid=po-entry-item-lookup]").Should().BeEmpty();
    }

    [Fact]
    public void PoEntry_VendorLookup_KeywordSearch_SelectFillsHeader()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-lookup]").Click();

        page.Find("[data-testid=po-entry-vendor-lookup-keyword]").Change("busan");
        page.Find("[data-testid=po-entry-vendor-lookup] form").Submit();
        page.FindAll("[data-testid=po-entry-vendor-lookup-grid-select]").Should().ContainSingle().Which.Click();

        Value(page, "po-entry-header-vendor-code").Should().Be("V0002");
        Value(page, "po-entry-header-vendor-name").Should().Be("Busan Electronics");
    }

    [Fact]
    public void PoEntry_UndoRow_RestoresLoadedValues()
    {
        var page = SearchPo(Render<SamplePoEntry>());
        page.FindAll("[data-testid=po-entry-line-qty]")[0].Change("9");
        page.Find("[data-state=U]").Should().NotBeNull();

        page.Find("[data-testid=po-entry-line-undo-row]").Click();

        Value(page, "po-entry-line-qty").Should().Be("2");
        page.FindAll("[data-state]").Should().BeEmpty();
        page.FindAll("[data-testid=po-entry-line-undo-row]").Should().BeEmpty();
    }

    [Fact]
    public void PoEntry_DeletedLine_IsLeftOutOfTotal()
    {
        var page = SearchPo(Render<SamplePoEntry>());

        page.FindAll("[data-testid=po-entry-line-delete-row]")[0].Click();

        Value(page, "po-entry-summary-total-amt").Should().Be("125.00");
    }

    [Fact]
    public void PoEntry_SaveNew_NumbersPo_AndStoresHeaderWithLines()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-code]").Change("v0003");
        page.Find("[data-testid=po-entry-line-add-row]").Click();
        page.Find("[data-testid=po-entry-line-item-code]").Change("MOU10001");
        page.Find("[data-testid=po-entry-line-qty]").Change("3");

        page.Find("[data-testid=po-entry-toolbar-save]").Click();

        var poNo = Value(page, "po-entry-header-po-no");
        poNo.Should().StartWith("PO");
        var saved = PoStore.FindPo(poNo)!.Value;
        saved.Po.VendorName.Should().Be("Global Parts Inc.");
        saved.Lines.Should().ContainSingle(l => l.ItemCode == "MOU10001" && l.Qty == 3 && l.Fob == 6.80m);
        page.FindAll("[data-state]").Should().BeEmpty();
    }

    [Fact]
    public void PoEntry_SaveWithoutVendor_ShowsRequiredMessage()
    {
        var page = Render<SamplePoEntry>();

        page.Find("[data-testid=po-entry-toolbar-save]").Click();

        page.Find("[data-testid=po-entry-message]").TextContent.Should().Contain("공급자을(를) 입력하세요.");
    }

    // MM

    [Fact]
    public void MasterDetail_SelectSource_LoadsTargets_AndAsksWhenTargetsChanged()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=master-detail-grid-select]").Should().HaveCount(2));

        page.FindAll("[data-testid=master-detail-grid-select]")[0].Click();
        page.FindAll("[data-testid=master-detail-line-item-code]").Should().ContainSingle();

        page.FindAll("[data-testid=master-detail-line-qty]")[0].Change("5");
        Confirm.Answer = false;
        page.FindAll("[data-testid=master-detail-grid-select]")[1].Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.FindAll("[data-testid=master-detail-line-item-code]").Should().ContainSingle();
    }

    [Fact]
    public void MasterDetail_SaveTargets_StoresForSelectedSource()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=master-detail-grid-select]").Should().HaveCount(2));
        page.FindAll("[data-testid=master-detail-grid-select]")[1].Click();

        page.Find("[data-testid=master-detail-toolbar-new]").Click();
        page.FindAll("[data-testid=master-detail-line-item-code]")[0].Change("b200");
        page.Find("[data-testid=master-detail-toolbar-save]").Click();

        Store.Members(2).Should().ContainSingle(m => m.ItemCode == "B200");
    }
}

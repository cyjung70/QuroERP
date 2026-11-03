using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

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
            .Should().Equal("single-toolbar-search", "single-toolbar-new", "single-toolbar-save", "single-toolbar-delete",
                "single-toolbar-copy", "single-toolbar-deactivate", "single-toolbar-activate");
    }

    [Fact]
    public void Single_Search_LoadsRecord_AndLocksNewOnlyField()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeFalse();

        page.Find("[data-testid=single-search-code]").Change("a100");
        page.Find("[data-testid=single-toolbar-search]").Click();

        page.Find("[data-testid=single-header-name] input").GetAttribute("value").Should().Be("Apple");
        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=single-toolbar-delete]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Single_TextCaseUpper_ConvertsInput()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-header-code] input").Change("abc");

        page.Find("[data-testid=single-header-code] input").GetAttribute("value").Should().Be("ABC");
    }

    [Fact]
    public void Single_SaveWithEmptyRequired_ShowsRequiredMessage_AndDoesNotSave()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code] input").Change("C300");

        page.Find("[data-testid=single-toolbar-save]").Click();

        page.Find("[data-testid=single-message]").TextContent.Should().Contain("이름을(를) 입력하세요.");
        Store.FindItem("C300").Should().BeNull();
    }

    [Fact]
    public void Single_SaveNew_StoresRecord()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code] input").Change("c300");
        page.Find("[data-testid=single-header-name] input").Change("Cherry");

        page.Find("[data-testid=single-toolbar-save]").Click();

        Store.FindItem("C300")!.Name.Should().Be("Cherry");
        page.Find("[data-testid=single-message]").TextContent.Should().Contain("저장했습니다.");
        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void Single_NewWithUnsavedChanges_AsksAndKeepsInputWhenCancelled()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-name] input").Change("Draft");
        Confirm.Answer = false;

        page.Find("[data-testid=single-toolbar-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.Find("[data-testid=single-header-name] input").GetAttribute("value").Should().Be("Draft");
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

    // The sample M grid is the QuroGrid package over its .toon definition (TASK-CORE-055)
    private static DataGridPage<SampleItem> Loaded(IRenderedComponent<SampleMulti> page)
    {
        var grid = page.DataGrid<SampleItem, SampleMulti>("multi-grid");
        page.WaitForAssertion(() => grid.Rows.Should().HaveCount(2));
        return grid;
    }

    [Fact]
    public void Multi_LoadsRows_WithToolbarInFixedOrder()
    {
        var page = Render<SampleMulti>();

        Loaded(page).Column("code").Should().Equal("A100", "B200");
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("multi-toolbar-search", "multi-toolbar-new", "multi-toolbar-save", "multi-toolbar-export");
    }

    [Theory, Trait("Id", "CORE-058-WT-06")]
    [InlineData("ko", "2026-01-01")]
    [InlineData("en", "01/01/2026")]
    public void Multi_DateColumn_UsesTheLanguageDatePattern(string lang, string shown)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(lang);

        var page = Render<SampleMulti>();

        Loaded(page).Text(0, "validFrom").Should().Be(shown);
    }

    [Fact]
    public void Multi_RowStates_AreShownAsIUD()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);

        grid.Edit(0, "name", "Apricot").Should().BeTrue();
        grid.Click(1, "deleteRow");
        page.Find("[data-testid=multi-toolbar-new]").Click();

        grid.Column("rowState").Should().Equal(State("Modified"), State("Deleted"), State("Added"));
    }

    [Fact]
    public void Multi_Save_SendsBatch_AndReloads()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Edit(0, "name", "Apricot");
        grid.Click(1, "deleteRow");
        page.Find("[data-testid=multi-toolbar-new]").Click();
        grid.Edit(2, "code", "c300").Should().BeTrue();
        grid.Edit(2, "name", "Cherry");

        page.Find("[data-testid=multi-toolbar-save]").Click();

        Store.Items().Select(i => (i.Code, i.Name)).Should().Equal(("A100", "Apricot"), ("C300", "Cherry"));
        grid.Rows.Should().HaveCount(2);
        grid.Column("rowState").Should().OnlyContain(s => s.Length == 0);
    }

    [Fact]
    public void Multi_SaveWithEmptyRequiredCell_ShowsRequiredMessage()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        page.Find("[data-testid=multi-toolbar-new]").Click();
        grid.Edit(2, "qty", "5");

        page.Find("[data-testid=multi-toolbar-save]").Click();

        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("코드을(를) 입력하세요.");
        Store.Items().Should().HaveCount(2);
    }

    [Fact]
    public void Multi_SaveWithoutChanges_SaysNoChanges()
    {
        var page = Render<SampleMulti>();
        Loaded(page);

        page.Find("[data-testid=multi-toolbar-save]").Click();

        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("저장할 변경 내용이 없습니다.");
    }

    [Fact]
    public void Multi_SearchWithUnsavedChanges_Asks()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Edit(0, "name", "Apricot");
        Confirm.Answer = false;

        page.Find("[data-testid=multi-toolbar-search]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        grid.Text(0, "rowState").Should().Be(State("Modified"));
    }

    private string State(string state) => Services.GetRequiredService<IStringLocalizer<SharedResource>>()[$"Common.RowState.{state}"];

    // SM (entry frame: the toolbar search opens the document search dialog; lines on the package grid, TASK-CORE-056)

    private static void OpenDocument<TPage, THeader>(IRenderedComponent<TPage> page, string screen, string keyword)
        where TPage : class, Microsoft.AspNetCore.Components.IComponent
        where THeader : class
    {
        page.Find($"[data-testid={screen}-toolbar-search]").Click();
        page.Find($"[data-testid={screen}-find-keyword]").Change(keyword);
        page.Find($"[data-testid={screen}-find] form").Submit();
        page.WaitForAssertion(() => page.LookupRows($"{screen}-find").Should().ContainSingle());
        page.PickLookupRow<THeader, TPage>($"{screen}-find", 0);
    }

    private static DataGridPage<SampleLine> OrderLines(IRenderedComponent<SampleHeaderDetail> page) =>
        page.DataGrid<SampleLine, SampleHeaderDetail>("header-detail-line");

    [Fact]
    public void HeaderDetail_SaveHeaderAndLines_InOneSave()
    {
        var page = Render<SampleHeaderDetail>();
        OpenDocument<SampleHeaderDetail, SampleOrder>(page, "header-detail", "SO261000001");
        var lines = OrderLines(page);
        page.WaitForAssertion(() => lines.Rows.Should().HaveCount(2));

        lines.Select(0);
        page.Find("[data-testid=header-detail-toolbar-mark-delete]").Click();
        page.Find("[data-testid=header-detail-toolbar-add-row]").Click();
        lines.Edit(1, "itemCode", "c300").Should().BeFalse("C300 is not an item, so the popup cell stays in edit");
        lines.Edit(1, "itemCode", "b2").Should().BeTrue("one item matches and is taken");
        page.Find("[data-testid=header-detail-toolbar-save]").Click();

        Store.FindOrder("SO261000001")!.Value.Lines.Select(l => l.ItemCode).Should().Equal("B200", "B200");
        lines.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void HeaderDetail_LineChangeOnly_IsUnsavedChange()
    {
        var page = Render<SampleHeaderDetail>();
        OpenDocument<SampleHeaderDetail, SampleOrder>(page, "header-detail", "SO261000001");
        var lines = OrderLines(page);
        page.WaitForAssertion(() => lines.Rows.Should().HaveCount(2));
        lines.Edit(0, "qty", "9");
        Confirm.Answer = false;

        page.Find("[data-testid=header-detail-toolbar-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
    }

    // SM example: PO Entry

    private static IRenderedComponent<SamplePoEntry> SearchPo(IRenderedComponent<SamplePoEntry> page)
    {
        OpenDocument<SamplePoEntry, SamplePo>(page, "po-entry", "PO261000001");
        page.WaitForAssertion(() => PoLines(page).Rows.Should().HaveCount(2));
        return page;
    }

    private static DataGridPage<SamplePoLine> PoLines(IRenderedComponent<SamplePoEntry> page) =>
        page.DataGrid<SamplePoLine, SamplePoEntry>("po-entry-line");

    private static QuroHeaderDetail<SamplePo, SamplePoLine> PoFrame(IRenderedComponent<SamplePoEntry> page) =>
        page.FindComponent<QuroHeaderDetail<SamplePo, SamplePoLine>>().Instance;

    private static string? Value(IRenderedComponent<SamplePoEntry> page, string testId) =>
        page.Find($"[data-testid={testId}] input").GetAttribute("value");

    private static string Text(IRenderedComponent<SamplePoEntry> page, string testId) =>
        page.Find($"[data-testid={testId}]").TextContent.Trim();

    [Fact]
    public void PoEntry_Search_LoadsHeaderLinesAndTotal()
    {
        var page = SearchPo(Render<SamplePoEntry>());

        Value(page, "po-entry-header-vendor-name").Should().Be("ABC Trading Co.");
        page.Find("[data-testid=po-entry-header-vendor-name] input").HasAttribute("readonly").Should().BeTrue();
        Text(page, "po-entry-doc-no").Should().Be("PO261000001");
        Text(page, "po-entry-status-chip").Should().Be("작성");
        page.Find("[data-testid=po-entry-totals-total] b").TextContent.Should().Be("1,825.00");
        PoLines(page).Column("amount").Should().Equal("1,700.00", "125.00");
    }

    [Fact]
    public void PoEntry_TypedItemCode_FillsNameAndDefaultPrice_AndUpdatesTotal()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-code] input").Change("v0001");
        page.Find("[data-testid=po-entry-toolbar-add-row]").Click();
        var lines = PoLines(page);

        lines.Edit(0, "itemCode", "kbd").Should().BeTrue("one item matches the typed text");
        lines.Edit(0, "qty", "4");

        lines.Text(0, "itemCode").Should().Be("KBD10001");
        lines.Text(0, "itemName").Should().Be("Keyboard USB");
        lines.Text(0, "fob").Should().Be("12.50");
        lines.Text(0, "amount").Should().Be("50.00");
        page.Find("[data-testid=po-entry-totals-total] b").TextContent.Should().Be("50.00");
    }

    [Fact]
    public async Task PoEntry_SeveralMatches_OpenTheItemLookup_PickFillsLine_AndCloses()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-code] input").Change("v0001");
        page.Find("[data-testid=po-entry-toolbar-add-row]").Click();
        var lines = PoLines(page);

        var commit = lines.EditAsync(0, "itemCode", "mo");
        page.WaitForAssertion(() => page.LookupRows("po-entry-line-lookup").Should().HaveCount(2));
        page.Find("[data-testid=po-entry-line-lookup-keyword]").GetAttribute("value").Should().Be("mo");
        page.PickLookupRow<LookupRow, SamplePoEntry>("po-entry-line-lookup", 0);

        (await commit).Should().BeTrue();
        lines.Text(0, "itemCode").Should().Be("MON24001");
        lines.Text(0, "fob").Should().Be("180.00");
        page.FindAll("[data-testid=po-entry-line-lookup]").Should().BeEmpty();
    }

    [Fact]
    public async Task PoEntry_F4_OpensTheLookup_CloseKeepsTheCell()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-code] input").Change("v0001");
        page.Find("[data-testid=po-entry-toolbar-add-row]").Click();
        var lines = PoLines(page);

        var popup = lines.F4Async(0, "itemCode", "KBD10001");
        // F4 opens the dialog even for one match
        page.WaitForAssertion(() => page.LookupRows("po-entry-line-lookup").Should().ContainSingle());
        page.Find("[data-testid=po-entry-line-lookup-close]").Click();

        (await popup).Should().BeFalse();
        lines.Text(0, "itemCode").Should().BeEmpty();
    }

    [Fact]
    public void PoEntry_ItemCode_WaitsForTheVendor()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-toolbar-add-row]").Click();

        PoLines(page).Edit(0, "itemCode", "KBD10001").Should().BeFalse();

        page.Find("[data-testid=po-entry-message]").TextContent.Should().Be("거래처를 먼저 선택하세요.");
    }

    [Fact]
    public void PoEntry_PastedCodes_AreReadTogether_UnknownCodesAreMarked_AndBlockSave()
    {
        var page = SearchPo(Render<SamplePoEntry>());
        var lines = PoLines(page);

        lines.Paste(0, "itemCode", ["mou10001"], ["NOPE"]).Should().BeTrue();

        lines.Text(0, "itemName").Should().Be("Mouse optical");
        lines.Text(0, "amount").Should().Be("13.60");
        lines.Rows[1].QuerySelector("[data-field=itemCode]")!.GetAttribute("aria-invalid").Should().Be("true");
        page.Find("[data-testid=po-entry-toolbar-save]").Click();
        page.Find("[data-testid=po-entry-message]").TextContent.Should().Be("없는 코드입니다.");
        PoStore.FindPo("PO261000001")!.Value.Lines[0].ItemCode.Should().Be("COMIX010");
    }

    [Fact]
    public void PoEntry_VendorLookup_KeywordSearch_SelectFillsHeader()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-lookup]").Click();

        page.Find("[data-testid=po-entry-vendor-lookup-keyword]").Change("busan");
        page.Find("[data-testid=po-entry-vendor-lookup] form").Submit();
        page.WaitForAssertion(() => page.LookupRows("po-entry-vendor-lookup").Should().ContainSingle());
        page.PickLookupRow<SampleLookupRow, SamplePoEntry>("po-entry-vendor-lookup", 0);

        Value(page, "po-entry-header-vendor-code").Should().Be("V0002");
        Value(page, "po-entry-header-vendor-name").Should().Be("Busan Electronics");
    }

    [Fact]
    public void PoEntry_RestoreRow_RestoresLoadedValues()
    {
        var page = SearchPo(Render<SamplePoEntry>());
        var lines = PoLines(page);
        lines.Edit(0, "qty", "9");
        PoFrame(page).Lines.GetState(lines.KeyOf(0)).Should().Be(Quro.Grid.Data.RowState.Updated);

        lines.Select(0);
        page.Find("[data-testid=po-entry-toolbar-restore-row]").Click();

        lines.Text(0, "qty").Should().Be("2.00");
        PoFrame(page).Lines.HasChanges.Should().BeFalse();
        page.Find("[data-testid=po-entry-toolbar-restore-row]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void PoEntry_MarkDelete_LeavesLineOutOfTotal_AndTogglesBack()
    {
        var page = SearchPo(Render<SamplePoEntry>());
        var lines = PoLines(page);
        lines.Select(0);

        page.Find("[data-testid=po-entry-toolbar-mark-delete]").Click();

        page.Find("[data-testid=po-entry-totals-total] b").TextContent.Should().Be("125.00");
        lines.Rows[0].ClassList.Should().Contain("quro-deleted");
        page.Find("[data-testid=po-entry-toolbar-mark-delete]").Click();
        page.Find("[data-testid=po-entry-totals-total] b").TextContent.Should().Be("1,825.00");
        PoFrame(page).Lines.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void PoEntry_QtyChange_CalculatesTheAmount_ForDisplay()
    {
        var page = SearchPo(Render<SamplePoEntry>());

        PoLines(page).Edit(1, "qty", "4");

        PoLines(page).Text(1, "amount").Should().Be("50.00");
        page.Find("[data-testid=po-entry-totals-total] b").TextContent.Should().Be("1,750.00");
    }

    [Fact]
    public void PoEntry_SaveNew_NumbersPo_AndStoresHeaderWithLines()
    {
        var page = Render<SamplePoEntry>();
        page.Find("[data-testid=po-entry-header-vendor-code] input").Change("v0003");
        page.Find("[data-testid=po-entry-toolbar-add-row]").Click();
        var lines = PoLines(page);
        lines.Edit(0, "itemCode", "MOU10001");
        lines.Edit(0, "qty", "3");

        page.Find("[data-testid=po-entry-toolbar-save]").Click();

        var poNo = Text(page, "po-entry-doc-no");
        poNo.Should().StartWith("PO");
        var saved = PoStore.FindPo(poNo)!.Value;
        saved.Po.VendorName.Should().Be("Global Parts Inc.");
        saved.Lines.Should().ContainSingle(l => l.ItemCode == "MOU10001" && l.Qty == 3 && l.Fob == 6.80m && l.Amount == 20.40m);
        PoFrame(page).Lines.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void PoEntry_SaveWithoutVendor_ShowsRequiredMessage()
    {
        var page = Render<SamplePoEntry>();

        page.Find("[data-testid=po-entry-toolbar-save]").Click();

        page.Find("[data-testid=po-entry-message]").TextContent.Should().Contain("공급자을(를) 입력하세요.");
    }

    [Fact, Trait("Id", "CORE-051-WT-06"), Trait("Rule", "BC-BR-214")]
    public void Multi_WithoutCanWrite_DisablesNewSaveAndRows_SearchStillWorks()
    {
        var page = Render<SampleMulti>(p => p.Add(x => x.CanWrite, false));
        var grid = Loaded(page);

        page.Find("[data-testid=multi-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=multi-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        grid.IsReadOnly(0, "name").Should().BeTrue();
        grid.Edit(0, "name", "Apricot").Should().BeFalse();
        grid.Click(1, "deleteRow");
        page.Find("[data-testid=multi-toolbar-new]").Click();
        grid.Column("name").Should().Equal("Apple", "Banana");
        grid.Column("rowState").Should().OnlyContain(s => s.Length == 0);

        page.Find("[data-testid=multi-toolbar-search]").Click();
        grid.Rows.Should().HaveCount(2);
    }

    [Fact, Trait("Id", "CORE-051-WT-07")]
    public void Multi_ListWithoutSave_HasNoNewOrSave_WhateverCanWrite()
    {
        foreach (var canWrite in new[] { true, false })
        {
            var page = Render<QuroMultiGrid<SampleItem>>(p => p
                .Add(x => x.Title, "List")
                .Add(x => x.TestId, "list")
                .Add(x => x.CanWrite, canWrite)
                .Add(x => x.Definition, "samples.item.multi")
                .Add(x => x.Load, () => Task.FromResult<IReadOnlyList<SampleItem>>([new SampleItem { Id = 1, Code = "A100" }])));

            page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
                .Should().Equal("list-toolbar-search", "list-toolbar-export");
        }
    }

    [Fact, Trait("Id", "CORE-051-WT-08")]
    public void Toolbar_WhileBusy_DisablesTheScreensOwnCommands()
    {
        RenderFragment extra = b =>
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "data-testid", "own");
            b.CloseElement();
        };

        var idle = Render<QuroToolbar>(p => p.Add(x => x.ExtraCommands, extra));
        var busy = Render<QuroToolbar>(p => p.Add(x => x.ExtraCommands, extra).Add(x => x.Busy, true));

        idle.Find("fieldset[data-group=extra]").HasAttribute("disabled").Should().BeFalse();
        busy.Find("fieldset[data-group=extra]").HasAttribute("disabled").Should().BeTrue();
        busy.Find("fieldset[data-group=extra] [data-testid=own]").Should().NotBeNull();
    }

    // MM (both grids on the package, TASK-CORE-056)

    private static DataGridPage<SampleGroup> Groups(IRenderedComponent<SampleMasterDetail> page) =>
        page.DataGrid<SampleGroup, SampleMasterDetail>("master-detail-grid");

    private static DataGridPage<SampleLine> Members(IRenderedComponent<SampleMasterDetail> page) =>
        page.DataGrid<SampleLine, SampleMasterDetail>("master-detail-line");

    private static void PickGroup(IRenderedComponent<SampleMasterDetail> page, int index) =>
        Groups(page).Click(index, QuroMasterMultiDetail<SampleGroup, SampleLine>.SelectField);

    [Fact]
    public void MasterDetail_SelectSource_LoadsTargets_AndAsksWhenTargetsChanged()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => Groups(page).Rows.Should().HaveCount(2));
        Groups(page).IsReadOnly(0, "name").Should().BeTrue("the source list is for picking only");

        PickGroup(page, 0);
        Members(page).Rows.Should().ContainSingle();

        Members(page).Edit(0, "qty", "5");
        Confirm.Answer = false;
        PickGroup(page, 1);

        Confirm.Questions.Should().Equal(Unsaved);
        Members(page).Column("itemCode").Should().Equal("A100");
    }

    [Fact]
    public void MasterDetail_SaveTargets_StoresForSelectedSource()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => Groups(page).Rows.Should().HaveCount(2));
        PickGroup(page, 1);

        page.Find("[data-testid=master-detail-toolbar-new]").Click();
        Members(page).Edit(0, "itemCode", "b200").Should().BeTrue();
        Members(page).Column("rowState").Should().Equal(State("Added"));
        page.Find("[data-testid=master-detail-toolbar-save]").Click();

        Store.Members(2).Should().ContainSingle(m => m.ItemCode == "B200");
    }

    [Fact]
    public void MasterDetail_DeleteAndUndo_OfATargetRow()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => Groups(page).Rows.Should().HaveCount(2));
        PickGroup(page, 0);

        Members(page).Click(0, "deleteRow");
        Members(page).Column("rowState").Should().Equal(State("Deleted"));
        Members(page).Select(0);
        page.Find("[data-testid=master-detail-line-restore-row]").Click();

        Members(page).Column("rowState").Should().OnlyContain(s => s.Length == 0);
    }

    [Fact, Trait("Id", "CORE-051-WT-09"), Trait("Rule", "BC-BR-214")]
    public void MasterDetail_WithoutCanWrite_SelectsAndShowsTargets_ButCannotChangeThem()
    {
        var page = Render<SampleMasterDetail>(p => p.Add(x => x.CanWrite, false));
        page.WaitForAssertion(() => Groups(page).Rows.Should().HaveCount(2));
        PickGroup(page, 0);

        var members = Members(page);
        members.Rows.Should().ContainSingle();
        members.IsReadOnly(0, "itemCode").Should().BeTrue();
        members.Edit(0, "qty", "5").Should().BeFalse();
        page.Find("[data-testid=master-detail-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=master-detail-toolbar-save]").HasAttribute("disabled").Should().BeTrue();

        page.Find("[data-testid=master-detail-toolbar-new]").Click();
        members.Click(0, "deleteRow");
        members.Rows.Should().ContainSingle();
        members.Column("rowState").Should().OnlyContain(s => s.Length == 0);
    }
}

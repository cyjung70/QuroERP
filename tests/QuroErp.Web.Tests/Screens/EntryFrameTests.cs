using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// The SM entry frame (TASK-CORE-031, entry-frame.md): five parts, command groups, disabled commands, field and row
/// colors, status chip, status commands and shortcuts. Uses the PO Entry sample; the lines are the package grid
/// (TASK-CORE-056).
/// </summary>
public class EntryFrameTests : ScreenContext
{
    private const string Screen = "po-entry";

    private IRenderedComponent<SamplePoEntry> Open(string poNo = "PO261000001")
    {
        var page = Render<SamplePoEntry>(p => p.Add(x => x.PoNo, poNo));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));
        return page;
    }

    private static DataGridPage<SamplePoLine> Lines(IRenderedComponent<SamplePoEntry> page) =>
        page.DataGrid<SamplePoLine, SamplePoEntry>("po-entry-line");

    private static QuroHeaderDetail<SamplePo, SamplePoLine> Frame(IRenderedComponent<SamplePoEntry> page) =>
        page.FindComponent<QuroHeaderDetail<SamplePo, SamplePoLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SamplePoEntry> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static Task Shortcut(IRenderedComponent<SamplePoEntry> page, string command)
    {
        var frame = page.FindComponent<QuroHeaderDetail<SamplePo, SamplePoLine>>();
        return frame.InvokeAsync(() => frame.Instance.OnShortcut(command));
    }

    [Fact]
    public void Frame_PartsInOrder_TitleCommandBarHeaderTotalsLinesAudit()
    {
        var page = Open();

        string[] parts = ["title", "toolbar", "header", "totals", "line", "audit"];
        page.FindAll("[data-testid]").Select(e => e.GetAttribute("data-testid"))
            .Where(id => parts.Any(part => id == $"{Screen}-{part}"))
            .Should().Equal(parts.Select(part => $"{Screen}-{part}"));
        page.Find("[data-testid=po-entry-title] .quro-entry-path").TextContent.Should().Be("샘플 > 구매");
    }

    [Fact]
    public void CommandBar_GroupsAndButtons_InFixedOrder()
    {
        var page = Open();

        page.FindAll(".quro-commandbar-group").Select(g => g.GetAttribute("data-group"))
            .Should().Equal("file", "rows", "status", "output");
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().Equal(
            "po-entry-toolbar-search", "po-entry-toolbar-new", "po-entry-toolbar-save",
            "po-entry-toolbar-add-row", "po-entry-toolbar-mark-delete", "po-entry-toolbar-restore-row",
            "po-entry-toolbar-approve", "po-entry-toolbar-delete");
        Button(page, "save").ClassList.Should().Contain("quro-primary");
        Button(page, "delete").ClassList.Should().Contain("quro-danger-text");
        Button(page, "delete").TextContent.Should().Be("발주 삭제");
    }

    [Fact]
    public void NewDocument_ShowsNewDocument_NoChip_EmptyAudit_AndDisablesSavedOnlyCommands()
    {
        var page = Render<SamplePoEntry>();

        page.Find("[data-testid=po-entry-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=po-entry-status-chip]").Should().BeEmpty();
        page.Find("[data-testid=po-entry-audit]").TextContent.Trim().Should().BeEmpty();
        Button(page, "delete").HasAttribute("disabled").Should().BeTrue();
        Button(page, "mark-delete").HasAttribute("disabled").Should().BeTrue();
        Button(page, "restore-row").HasAttribute("disabled").Should().BeTrue();
        Button(page, "save").HasAttribute("disabled").Should().BeFalse();
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("공급자를 먼저 고르세요.");
    }

    [Fact]
    public void Fields_ColorClasses_FollowRequiredAndLockMode()
    {
        var page = Render<SamplePoEntry>();

        Field(page, "vendor-code").ClassList.Should().Contain(["quro-field-editable", "quro-field-required"]);
        Field(page, "vendor-name").ClassList.Should().Contain("quro-field-readonly").And.Contain("quro-span-2");
        Field(page, "vendor-message").ClassList.Should().Contain("quro-field-editable").And.Contain("quro-span-4")
            .And.NotContain("quro-field-required");
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<SamplePoEntry> page, string prop) =>
        page.Find($"[data-testid=po-entry-header-{prop}]").Closest(".quro-field")!;

    [Fact]
    public void AddRow_InsertsBelowSelectedLine_AndSelectsIt()
    {
        var page = Open();
        var lines = Lines(page);
        lines.Select(0);

        Button(page, "add-row").Click();

        lines.Column("itemCode").Should().Equal("COMIX010", "", "KBD10001");
        var added = lines.KeyOf(1);
        Frame(page).Lines.CurrentRowKey.Should().Be(added);
        Frame(page).Lines.GetState(added).Should().Be(Quro.Grid.Data.RowState.Inserted);
    }

    [Fact]
    public void MarkDelete_NewLine_IsRemovedAtOnce_ASavedLineIsMarkedAndUnmarked()
    {
        var page = Open();
        var lines = Lines(page);
        Button(page, "add-row").Click();

        Button(page, "mark-delete").Click();

        lines.Rows.Should().HaveCount(2);
        Frame(page).Lines.HasChanges.Should().BeFalse();

        lines.Select(0);
        Button(page, "mark-delete").Click();
        Frame(page).Lines.GetState(lines.KeyOf(0)).Should().Be(Quro.Grid.Data.RowState.Deleted);
        Button(page, "mark-delete").GetAttribute("aria-pressed").Should().Be("true");
        Button(page, "mark-delete").Click();
        Frame(page).Lines.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Approve_ShowsChip_LocksDocument_AndDisablesBlockedCommands()
    {
        var page = Open();
        page.Find("[data-testid=po-entry-status-chip] .quro-chip").ClassList.Should().Contain("quro-chip-neutral");

        Button(page, "approve").Click();

        Confirm.Questions.Should().Equal("발주를 승인할까요?");
        var chip = page.Find("[data-testid=po-entry-status-chip] .quro-chip");
        chip.GetAttribute("data-status").Should().Be("AP");
        chip.ClassList.Should().Contain("quro-chip-brand");
        chip.TextContent.Should().Be("승인");
        foreach (var action in new[] { "save", "add-row", "mark-delete", "restore-row", "approve", "delete" })
        {
            Button(page, action).HasAttribute("disabled").Should().BeTrue(action);
        }
        Field(page, "vendor-code").ClassList.Should().Contain("quro-field-readonly");
        Lines(page).IsReadOnly(0, "qty").Should().BeTrue("an approved PO's lines are locked");
        Lines(page).Edit(0, "qty", "9").Should().BeFalse();
        PoStore.FindPo("PO261000001")!.Value.Po.Status.Should().Be(SamplePoStore.Approved);
    }

    [Fact]
    public void StatusCommand_WithUnsavedChanges_AsksToSaveFirst()
    {
        var page = Open();
        Lines(page).Edit(0, "qty", "9");

        Button(page, "approve").Click();

        var message = page.Find("[data-testid=po-entry-message]");
        message.TextContent.Should().Be("변경 내용을 먼저 저장하세요.");
        message.GetAttribute("data-level").Should().Be("warn");
        Confirm.Questions.Should().BeEmpty();
        PoStore.FindPo("PO261000001")!.Value.Po.Status.Should().Be(SamplePoStore.Draft);
    }

    [Fact]
    public void SaveLoadedDocument_ShowsRevisionAndAudit()
    {
        var page = Open();
        page.FindAll("[data-testid=po-entry-revision]").Should().BeEmpty();

        Lines(page).Edit(0, "qty", "3");
        Button(page, "save").Click();

        page.Find("[data-testid=po-entry-revision]").TextContent.Should().Be("Rev. 1");
        page.Find("[data-testid=po-entry-audit]").TextContent.Should().Contain("수정").And.Contain("sample");
        page.Find("[data-testid=po-entry-message]").GetAttribute("data-level").Should().Be("info");
    }

    [Fact]
    public void Address_WithDocumentNumber_OpensDocument_UnknownNumber_ShowsNoData()
    {
        Open().Find("[data-testid=po-entry-doc-no]").TextContent.Should().Be("PO261000001");

        var unknown = Render<SamplePoEntry>(p => p.Add(x => x.PoNo, "PO000000000"));

        unknown.Find("[data-testid=po-entry-doc-no]").TextContent.Should().Be("새 문서");
        unknown.Find("[data-testid=po-entry-message]").TextContent.Should().Be("조회된 내용이 없습니다.");
    }

    [Fact]
    public void Search_WithUnsavedChanges_AsksBeforeOpeningDialog()
    {
        var page = Open();
        Lines(page).Edit(0, "qty", "9");
        Confirm.Answer = false;

        Button(page, "search").Click();

        Confirm.Questions.Should().ContainSingle();
        page.FindAll("[data-testid=po-entry-find]").Should().BeEmpty();
    }

    [Fact]
    public async Task Shortcuts_RunEnabledCommands_AndIgnoreDisabledOnes()
    {
        var page = Open();
        JSInterop.VerifyInvoke("quroEntryShortcuts");

        await Shortcut(page, "add-row");
        Lines(page).Rows.Should().HaveCount(3);
        await Shortcut(page, "mark-delete");
        Lines(page).Rows.Should().HaveCount(2);
        await Shortcut(page, "search");
        page.FindAll("[data-testid=po-entry-find]").Should().ContainSingle();

        page.Find("[data-testid=po-entry-find-close]").Click();
        Button(page, "approve").Click();
        await Shortcut(page, "add-row");
        Lines(page).Rows.Should().HaveCount(2);
    }

    [Fact]
    public void LockedLine_CannotBeEdited_OrMarked()
    {
        var store = new SampleStore();
        var page = Render<QuroHeaderDetail<SampleOrder, SampleLine>>(p => p
            .Add(x => x.Title, "T")
            .Add(x => x.TestId, "t")
            .Add(x => x.Definition, "samples.order.entry")
            .Add(x => x.HeaderFields, (RenderFragment<SampleOrder>)(_ => _ => { }))
            .Add(x => x.FindColumns, (RenderFragment)(_ => { }))
            .Add(x => x.Find, _ => Task.FromResult<IReadOnlyList<SampleOrder>>([]))
            .Add(x => x.Load, _ => Task.FromResult<HeaderLines<SampleOrder, SampleLine>?>(null))
            .Add(x => x.Initial, () => Task.FromResult(store.FindOrder("SO261000001") is { } o
                ? new HeaderLines<SampleOrder, SampleLine>(o.Order, o.Lines) : null))
            .Add(x => x.Create, () => new SampleOrder())
            .Add(x => x.CreateLine, _ => new SampleLine())
            .Add(x => x.Save, (o, _, _) => Task.FromResult(new HeaderLines<SampleOrder, SampleLine>(o, [])))
            .Add(x => x.IsLineLocked, l => l.ItemCode == "A100"));
        var lines = page.DataGrid<SampleLine, QuroHeaderDetail<SampleOrder, SampleLine>>("t-line");
        page.WaitForAssertion(() => lines.Rows.Should().HaveCount(2));

        lines.Edit(0, "qty", "9").Should().BeFalse();
        lines.Edit(1, "qty", "9").Should().BeTrue();
        lines.Select(0);
        page.Find("[data-testid=t-toolbar-mark-delete]").HasAttribute("disabled").Should().BeTrue();
        lines.Select(1);
        page.Find("[data-testid=t-toolbar-mark-delete]").HasAttribute("disabled").Should().BeFalse();
    }
}

/// <summary>Totals panel and status chip of the entry frame (entry-frame.md 4, 6).</summary>
public class EntryPartsTests : ScreenContext
{
    [Fact]
    public void Totals_FixedOrder_TotalIsSubtotalLessDiscountPlusTax_DiscountRowOnlyWhenNotZero()
    {
        var totals = Render<QuroTotals>(p => p
            .Add(x => x.Subtotal, 1000m).Add(x => x.Discount, 100m).Add(x => x.Tax, 90m)
            .Add(x => x.CurrencyCode, "USD").Add(x => x.TestId, "t"));

        totals.FindAll("[data-testid]").Select(e => e.GetAttribute("data-testid"))
            .Should().Equal("t-subtotal", "t-discount", "t-tax", "t-total");
        totals.Find("[data-testid=t-discount] b").TextContent.Should().Be("-100.00");
        totals.Find("[data-testid=t-total] b").TextContent.Should().Be("990.00");
        totals.Find("[data-testid=t-total] small").TextContent.Should().Be("USD");

        var noDiscount = Render<QuroTotals>(p => p.Add(x => x.Subtotal, 5m).Add(x => x.Decimals, 0).Add(x => x.TestId, "t"));
        noDiscount.FindAll("[data-testid=t-discount]").Should().BeEmpty();
        noDiscount.Find("[data-testid=t-total] b").TextContent.Should().Be("5");
    }

    [Fact]
    public void Totals_BaseTotal_OnlyWhenCurrencyDiffers()
    {
        var foreign = Render<QuroTotals>(p => p.Add(x => x.Subtotal, 10m).Add(x => x.CurrencyCode, "USD")
            .Add(x => x.BaseTotal, 13500m).Add(x => x.BaseCurrencyCode, "KRW").Add(x => x.TestId, "t"));
        var local = Render<QuroTotals>(p => p.Add(x => x.Subtotal, 10m).Add(x => x.CurrencyCode, "KRW")
            .Add(x => x.BaseTotal, 10m).Add(x => x.BaseCurrencyCode, "KRW").Add(x => x.TestId, "t"));

        foreign.Find("[data-testid=t-base-total]").TextContent.Should().Contain("13,500").And.Contain("KRW");
        local.FindAll("[data-testid=t-base-total]").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChipKind.Neutral, "quro-chip-neutral")]
    [InlineData(ChipKind.Brand, "quro-chip-brand")]
    [InlineData(ChipKind.Warn, "quro-chip-warn")]
    [InlineData(ChipKind.Ok, "quro-chip-ok")]
    [InlineData(ChipKind.Danger, "quro-chip-danger")]
    public void StatusChip_KindClass_AndStatusCode(ChipKind kind, string css)
    {
        var chip = Render<QuroStatusChip>(p => p.Add(x => x.Code, "DR").Add(x => x.Kind, kind).Add(x => x.Text, "Draft"));

        chip.Find("span").ClassList.Should().Contain(["quro-chip", css]);
        chip.Find("span").GetAttribute("data-status").Should().Be("DR");
        chip.Find("span").TextContent.Should().Be("Draft");
    }
}

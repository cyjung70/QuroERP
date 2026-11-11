using Bunit;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Editable M and MM screens handle new and the row commands as an entry screen does (screens.md "M·MM 행 명령").</summary>
public class MultiGridCommandTests : ScreenContext
{
    private const string Unsaved = "저장하지 않은 변경 내용이 있습니다. 계속하면 변경 내용이 사라집니다. 계속할까요?";

    private static DataGridPage<SampleItem> Loaded(IRenderedComponent<SampleMulti> page)
    {
        var grid = page.DataGrid<SampleItem, SampleMulti>("multi-grid");
        page.WaitForAssertion(() => grid.Rows.Should().HaveCount(2));
        return grid;
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SampleMulti> page, string action) =>
        page.Find($"[data-testid=multi-toolbar-{action}]");

    [Fact]
    public void Toolbar_HasTheRowGroup_WithIcons_AndTheGridHasNoRowButtons()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);

        page.FindAll(".quro-commandbar-group").Select(g => g.GetAttribute("data-group")).Should().Equal("standard", "rows", "output");
        Button(page, "add-row").QuerySelector(".quro-command-icon")!.GetAttribute("data-icon").Should().Be("add-row");
        Button(page, "mark-delete").QuerySelector(".quro-command-icon")!.GetAttribute("data-icon").Should().Be("mark-delete");
        Button(page, "restore-row").QuerySelector(".quro-command-icon")!.GetAttribute("data-icon").Should().Be("restore-line");
        grid.Headers.Select(h => h.Field).Should().Contain("rowState").And.NotContain(["deleteRow", "undoRow"]);
    }

    [Fact]
    public void New_EmptiesTheGrid_WithoutAddingARow()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);

        Button(page, "new").Click();

        grid.Rows.Should().BeEmpty();
        Confirm.Questions.Should().BeEmpty();
        Button(page, "save").Click();
        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("저장할 변경 내용이 없습니다.");
    }

    [Fact]
    public void New_WithUnsavedChanges_AsksFirst_AndARefusalKeepsThem()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Edit(0, "name", "Apricot");
        Confirm.Answer = false;

        Button(page, "new").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        grid.Column("name").Should().Equal("Apricot", "Banana");
    }

    [Fact]
    public void AddRow_GoesBelowTheSelectedRow_AndBecomesSelected()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Select(0);

        Button(page, "add-row").Click();

        grid.Column("code").Should().Equal("A100", "", "B200");
        Button(page, "restore-row").HasAttribute("disabled").Should().BeFalse("the new row is the selected row");
    }

    [Fact]
    public void MarkDelete_MarksASavedRow_AndTogglesOff()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Select(1);
        Button(page, "mark-delete").GetAttribute("aria-pressed").Should().Be("false");

        Button(page, "mark-delete").Click();

        grid.Rows.Should().HaveCount(2);
        grid.Text(1, "rowState").Should().Be("삭제");
        Button(page, "mark-delete").GetAttribute("aria-pressed").Should().Be("true");

        Button(page, "mark-delete").Click();

        grid.Text(1, "rowState").Should().BeEmpty();
    }

    [Fact]
    public void RestoreRow_IsDisabledOnAnUnchangedRow_AndRemovesANewRow()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Select(0);
        Button(page, "restore-row").HasAttribute("disabled").Should().BeTrue();

        Button(page, "add-row").Click();
        Button(page, "restore-row").Click();

        grid.Column("code").Should().Equal("A100", "B200");
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public void WithoutCanWrite_NewAndTheRowCommands_AreDisabled()
    {
        var page = Render<SampleMulti>(p => p.Add(x => x.CanWrite, false));
        var grid = Loaded(page);
        grid.Select(0);

        foreach (var action in new[] { "new", "add-row", "mark-delete", "restore-row" })
        {
            Button(page, action).HasAttribute("disabled").Should().BeTrue(action);
        }
    }

    [Fact]
    public void MasterDetail_New_EmptiesBothGrids_AndRowCommandsWaitForASource()
    {
        var page = Render<SampleMasterDetail>();
        var groups = page.DataGrid<SampleGroup, SampleMasterDetail>("master-detail-grid");
        page.WaitForAssertion(() => groups.Rows.Should().HaveCount(2));
        groups.Click(0, QuroMasterMultiDetail<SampleGroup, SampleLine>.SelectField);
        var members = page.DataGrid<SampleLine, SampleMasterDetail>("master-detail-line");
        page.WaitForAssertion(() => members.Rows.Should().ContainSingle());
        page.Find("[data-testid=master-detail-toolbar-add-row]").HasAttribute("disabled").Should().BeFalse();

        page.Find("[data-testid=master-detail-toolbar-new]").Click();

        groups.Rows.Should().BeEmpty();
        page.FindAll("[data-testid=master-detail-line]").Should().BeEmpty();
        page.Find("[data-testid=master-detail-toolbar-add-row]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=master-detail-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MasterDetail_AddRow_GoesBelowTheSelectedRow_AndHasNoRowButtons()
    {
        var page = Render<SampleMasterDetail>();
        var groups = page.DataGrid<SampleGroup, SampleMasterDetail>("master-detail-grid");
        page.WaitForAssertion(() => groups.Rows.Should().HaveCount(2));
        groups.Click(0, QuroMasterMultiDetail<SampleGroup, SampleLine>.SelectField);
        var members = page.DataGrid<SampleLine, SampleMasterDetail>("master-detail-line");
        page.WaitForAssertion(() => members.Rows.Should().ContainSingle());
        members.Select(0);

        page.Find("[data-testid=master-detail-toolbar-add-row]").Click();

        members.Rows.Should().HaveCount(2);
        members.Text(1, "rowState").Should().Be("신규");
        members.Headers.Select(h => h.Field).Should().NotContain(["deleteRow", "undoRow"]);
    }
}

using ClosedXML.Excel;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Quro.Grid;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// The M frame over the QuroGrid package and a screen definition (TASK-CORE-055, qurogrid-integration.md 10): row commands,
/// the permission lock, Excel and server paging.
/// </summary>
public class MultiGridDefinitionTests : ScreenContext
{
    private static DataGridPage<SampleItem> Loaded(IRenderedComponent<SampleMulti> page)
    {
        var grid = page.DataGrid<SampleItem, SampleMulti>("multi-grid");
        page.WaitForAssertion(() => grid.Rows.Should().HaveCount(2));
        return grid;
    }

    [Fact]
    public async Task Undo_RestoresAnEditedRow_AndRestore_TheSelectedDeletedRow()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        grid.Edit(0, "name", "Apricot");
        grid.Click(1, "deleteRow");
        page.Find("[data-testid=multi-grid-restore-row]").HasAttribute("disabled").Should().BeTrue();

        grid.Click(0, "undoRow");
        var cut = page.FindComponent<QuroGrid<SampleItem>>();
        await cut.InvokeAsync(() => cut.Instance.SelectionChanged(grid.KeyOf(1)));
        page.Find("[data-testid=multi-grid-restore-row]").Click();

        grid.Column("name").Should().Equal("Apple", "Banana");
        grid.Column("rowState").Should().OnlyContain(s => s.Length == 0);
        page.Find("[data-testid=multi-toolbar-save]").Click();
        page.Find("[data-testid=multi-message]").TextContent.Should().Contain("저장할 변경 내용이 없습니다.");
    }

    [Fact]
    public void Delete_OfANewRow_TakesItOut()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        page.Find("[data-testid=multi-toolbar-new]").Click();

        grid.Click(2, "deleteRow");

        grid.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Code_IsKeptInUpperCase_AndLocksOnceSaved()
    {
        var page = Render<SampleMulti>();
        var grid = Loaded(page);
        page.Find("[data-testid=multi-toolbar-new]").Click();

        grid.Edit(2, "code", "c300");
        grid.Text(2, "code").Should().Be("C300");
        grid.IsReadOnly(0, "code").Should().BeTrue("a loaded row's code is new-row only");
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public async Task WithoutCanWrite_ARowTheGridAdds_IsTakenOutAgain()
    {
        var page = Render<SampleMulti>(p => p.Add(x => x.CanWrite, false));
        var grid = Loaded(page);
        var cut = page.FindComponent<QuroGrid<SampleItem>>();

        await cut.InvokeAsync(() => cut.Instance.AddRow());

        grid.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Export_HasTheDefinitionColumns_WithTranslatedHeaders()
    {
        byte[]? file = null;
        JSInterop.SetupVoid("quroDownload", invocation =>
        {
            using var copy = new MemoryStream();
            ((DotNetStreamReference)invocation.Arguments[1]!).Stream.CopyTo(copy);
            file = copy.ToArray();
            return true;
        });
        var page = Render<SampleMulti>();
        Loaded(page);

        page.Find("[data-testid=multi-toolbar-export]").Click();

        page.WaitForAssertion(() => file.Should().NotBeNull());
        using var workbook = new XLWorkbook(new MemoryStream(file!));
        var sheet = workbook.Worksheet(1);
        Enumerable.Range(1, 4).Select(c => sheet.Cell(1, c).GetString()).Should().Equal("코드", "이름", "적용일", "수량");
        sheet.Cell(2, 1).GetString().Should().Be("A100");
        sheet.Cell(4, 1).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public async Task LoadPage_ReadsPagesOnTheServer_WithTheHeaderFilters()
    {
        var requests = new List<GridItemsRequest>();
        var page = Render<QuroMultiGrid<SampleItem>>(p => p
            .Add(x => x.Title, "Items")
            .Add(x => x.TestId, "items")
            .Add(x => x.Definition, "samples.item.multi")
            .Add(x => x.Load, () => Task.FromResult<IReadOnlyList<SampleItem>>([]))
            .Add(x => x.LoadPage, request =>
            {
                requests.Add(request);
                return Task.FromResult(new GridItemsResult<SampleItem>([new SampleItem { Id = 1, Code = "A100", Name = "Apple" }], 1));
            }));
        page.WaitForAssertion(() => requests.Should().NotBeEmpty());
        var cut = page.FindComponent<QuroGrid<SampleItem>>();

        await cut.InvokeAsync(async () =>
        {
            cut.Instance.SetFilters([new ColumnFilter("code") { Contains = "A1" }]);
            await cut.Instance.ReloadAsync();
        });

        page.WaitForAssertion(() => requests[^1].Filters.Should().ContainSingle().Which.Contains.Should().Be("A1"));
    }
}

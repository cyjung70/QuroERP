using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Contracts.Common;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>QuroMultiGrid.RunActionAsync: a screen action with the grid's busy state and messages (TASK-BC-022).</summary>
public class QuroMultiGridActionTests : ScreenContext
{
    private readonly List<Row> _stored = [new("A")];
    private int _loads;

    [Fact, Trait("Id", "BC-022-WT-01")]
    public async Task RunAction_ShowsTheNotice_AndReadsTheRowsAgain()
    {
        var grid = RenderGrid();

        await grid.InvokeAsync(() => grid.Instance.RunActionAsync(() =>
        {
            _stored.Add(new Row("B"));
            return Task.FromResult<string?>("done");
        }));

        grid.Find("[data-testid=t-message]").TextContent.Should().Be("done");
        grid.Instance.Items.Select(r => r.Code).Should().Equal("A", "B");
        _loads.Should().Be(2);
    }

    [Fact, Trait("Id", "BC-022-WT-02")]
    public async Task RunAction_ApiProblem_ShowsItsMessage_AndKeepsTheRows()
    {
        var grid = RenderGrid();

        await grid.InvokeAsync(() => grid.Instance.RunActionAsync(
            () => throw new ApiProblemException(new ApiProblem(422, "MM-E-817", ["B200"]))));

        grid.Find("[data-testid=t-message]").TextContent.Should().Be("바코드가 다른 품목(B200)과 겹칩니다. 아무것도 저장하지 않았습니다.");
        grid.Instance.Items.Select(r => r.Code).Should().Equal("A");
        _loads.Should().Be(1);
    }

    private IRenderedComponent<QuroMultiGrid<Row>> RenderGrid()
    {
        var grid = Render<QuroMultiGrid<Row>>(p => p
            .Add(g => g.Title, "T")
            .Add(g => g.TestId, "t")
            .Add(g => g.LoadOnStart, true)
            .Add(g => g.Load, () =>
            {
                _loads++;
                return Task.FromResult<IReadOnlyList<Row>>(_stored.ToList());
            })
            .Add(g => g.Columns, (RenderFragment)(b =>
            {
                b.OpenComponent<QuroColumn<Row>>(0);
                b.AddAttribute(1, nameof(QuroColumn<Row>.Title), "Code");
                b.AddAttribute(2, nameof(QuroColumn<Row>.Value), (Func<Row, object?>)(r => r.Code));
                b.CloseComponent();
            })));
        grid.WaitForAssertion(() => _loads.Should().Be(1));
        return grid;
    }

    public sealed record Row(string Code);
}

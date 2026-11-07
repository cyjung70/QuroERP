using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>A row double-click in a lookup dialog picks the row as its select button does (screens.md "팝업").</summary>
public class LookupDoubleClickTests : ScreenContext
{
    private const string Listener = "quroListRowDoubleClick";
    private const string GridId = "find-grid";

    private static readonly IReadOnlyList<Row> Found = [new("A100"), new("B200")];

    public sealed record Row(string Code);

    private static RenderFragment Columns => builder =>
    {
        builder.OpenComponent<QuroColumn<Row>>(0);
        builder.AddAttribute(1, nameof(QuroColumn<Row>.Title), "Code");
        builder.AddAttribute(2, nameof(QuroColumn<Row>.Value), (Func<Row, object?>)(r => r.Code));
        builder.CloseComponent();
    };

    private IRenderedComponent<QuroLookupDialog<Row>> Dialog(Func<Row, Task> onSelected, Action onClosed)
    {
        var page = Render<QuroLookupDialog<Row>>(p => p
            .Add(x => x.Title, "Find")
            .Add(x => x.TestId, "find")
            .Add(x => x.Visible, true)
            .Add(x => x.Search, _ => Task.FromResult(Found))
            .Add(x => x.Columns, Columns)
            .Add(x => x.OnSelected, onSelected)
            .Add(x => x.VisibleChanged, (bool visible) =>
            {
                if (!visible)
                {
                    onClosed();
                }
            }));
        page.WaitForAssertion(() => page.LookupRows("find").Should().HaveCount(2));
        return page;
    }

    private static int KeyOf(IRenderedComponent<QuroLookupDialog<Row>> page, int index) =>
        page.DataGrid<Row, QuroLookupDialog<Row>>(GridId).KeyOf(index);

    private static Task DoubleClick(IRenderedComponent<QuroLookupDialog<Row>> page, int rowKey)
    {
        var list = page.FindComponent<QuroListGrid<Row>>();
        return list.InvokeAsync(() => list.Instance.OnRowDoubleClick(rowKey));
    }

    [Fact]
    public async Task RowKey_PicksThatRow_AndClosesTheDialog()
    {
        var picked = new List<Row>();
        var closed = false;
        var page = Dialog(r => { picked.Add(r); return Task.CompletedTask; }, () => closed = true);

        await DoubleClick(page, KeyOf(page, 1));

        picked.Should().Equal(Found[1]);
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownRowKey_DoesNothing()
    {
        var picked = new List<Row>();
        var closed = false;
        var page = Dialog(r => { picked.Add(r); return Task.CompletedTask; }, () => closed = true);

        await DoubleClick(page, -1);

        picked.Should().BeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task SecondDoubleClick_WhileTheFirstIsSelecting_DoesNothing()
    {
        var picked = new List<Row>();
        var gate = new TaskCompletionSource();
        var page = Dialog(async r => { picked.Add(r); await gate.Task; }, () => { });
        var key = KeyOf(page, 0);

        var first = DoubleClick(page, key);
        await DoubleClick(page, key);
        gate.SetResult();
        await first;

        picked.Should().Equal(Found[0]);
    }

    [Fact]
    public void SelectButton_StaysForTheKeyboard()
    {
        var picked = new List<Row>();
        var closed = false;
        var page = Dialog(r => { picked.Add(r); return Task.CompletedTask; }, () => closed = true);

        page.PickLookupRow<Row, QuroLookupDialog<Row>>("find", 0);

        picked.Should().Equal(Found[0]);
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task WhileBusy_ADoubleClickDoesNothing()
    {
        var picked = new List<Row>();
        var list = Render<QuroListGrid<Row>>(p => p
            .Add(x => x.Items, Found)
            .Add(x => x.Columns, Columns)
            .Add(x => x.TestId, GridId)
            .Add(x => x.Busy, true)
            .Add(x => x.OnSelect, (Row r) => picked.Add(r)));
        var key = list.DataGrid<Row, QuroListGrid<Row>>(GridId).KeyOf(0);

        await list.InvokeAsync(() => list.Instance.OnRowDoubleClick(key));

        picked.Should().BeEmpty();
    }

    [Fact]
    public void Listener_IsAttachedOnlyToAListThatSelects()
    {
        var page = Dialog(_ => Task.CompletedTask, () => { });
        JSInterop.Invocations[Listener].Should().ContainSingle();

        Render<QuroListGrid<Row>>(p => p.Add(x => x.Items, Found).Add(x => x.Columns, Columns));

        JSInterop.Invocations[Listener].Should().ContainSingle();
        page.LookupRows("find").Should().HaveCount(2);
    }
}

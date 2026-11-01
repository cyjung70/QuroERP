using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// The SM lines on the QuroGrid package (TASK-CORE-056, screens.md "그리드 코드 열"·"그리드 붙여 넣기"): code cells read the
/// screen's lookup, pasted codes are read in one call, and the definition's rules calculate the display amount.
/// </summary>
public class LineGridTests : ScreenContext
{
    private readonly List<LookupQuery> _queries = [];

    private Task<IReadOnlyList<LookupRow>> Lookup(LookupQuery query)
    {
        _queries.Add(query);
        var items = query.Codes is { } codes ? codes.Select(PoStore.Item).OfType<SampleLookupRow>() : PoStore.FindItems(query.Keyword);
        IReadOnlyList<LookupRow> rows = [.. items.Select(i => LookupRow.Of(("itemCode", i.Code), ("itemName", i.Name), ("fob", i.DefaultPrice)))];
        return Task.FromResult(rows);
    }

    private IRenderedComponent<QuroHeaderDetail<SamplePo, SamplePoLine>> Open() =>
        Render<QuroHeaderDetail<SamplePo, SamplePoLine>>(p => p
            .Add(x => x.Title, "T")
            .Add(x => x.TestId, "t")
            .Add(x => x.Definition, "samples.po.entry")
            .Add(x => x.Lookup, Lookup)
            .Add(x => x.UpperCaseFields, ["itemCode"])
            .Add(x => x.HeaderFields, (RenderFragment<SamplePo>)(_ => _ => { }))
            .Add(x => x.FindColumns, (RenderFragment)(_ => { }))
            .Add(x => x.Find, _ => Task.FromResult<IReadOnlyList<SamplePo>>([]))
            .Add(x => x.Load, _ => Task.FromResult<HeaderLines<SamplePo, SamplePoLine>?>(null))
            .Add(x => x.Initial, () => Task.FromResult(PoStore.FindPo("PO261000001") is { } po
                ? new HeaderLines<SamplePo, SamplePoLine>(po.Po, po.Lines) : null))
            .Add(x => x.Create, () => new SamplePo())
            .Add(x => x.CreateLine, _ => new SamplePoLine())
            .Add(x => x.Save, (po, _, _) => Task.FromResult(new HeaderLines<SamplePo, SamplePoLine>(po, []))));

    private static DataGridPage<SamplePoLine> Lines(IRenderedComponent<QuroHeaderDetail<SamplePo, SamplePoLine>> page)
    {
        var lines = page.DataGrid<SamplePoLine, QuroHeaderDetail<SamplePo, SamplePoLine>>("t-line");
        page.WaitForAssertion(() => lines.Rows.Should().HaveCount(2));
        return lines;
    }

    [Fact]
    public void PastedCodes_AreReadInOneCall_AndFillEveryLine()
    {
        var page = Open();
        var lines = Lines(page);
        _queries.Clear();

        // The third code adds a line (AddRowsOnPaste)
        lines.Paste(0, "itemCode", ["mou10001"], ["mon24001"], ["kbd10001"]).Should().BeTrue();

        _queries.Should().ContainSingle().Which.Codes.Should().Equal("MOU10001", "MON24001", "KBD10001");
        lines.Column("itemName").Should().Equal("Mouse optical", "Monitor 24 inch", "Keyboard USB");
        lines.Column("amount").Should().Equal("13.60", "1,800.00", "0.00");
    }

    [Fact]
    public void TypedCode_WithOneMatch_IsTaken_WithoutTheDialog()
    {
        var page = Open();
        var lines = Lines(page);
        _queries.Clear();

        lines.Edit(1, "itemCode", "mon").Should().BeTrue();

        _queries.Select(q => q.Keyword).Should().Contain("mon");
        lines.Text(1, "itemCode").Should().Be("MON24001");
        lines.Text(1, "amount").Should().Be("1,800.00", "the default price fills F.O.B and the amount follows");
        page.FindAll("[data-testid=t-line-lookup]").Should().BeEmpty();
    }

    [Fact]
    public void TypedCode_WithNoMatch_StaysInEdit_AndChangesNothing()
    {
        var page = Open();
        var lines = Lines(page);

        lines.Edit(1, "itemCode", "zzz").Should().BeFalse();

        lines.Text(1, "itemCode").Should().Be("KBD10001");
        page.Instance.Lines.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Totals_LeaveOutDeletedLines_AndFollowTheCalculatedAmount()
    {
        IReadOnlyList<SamplePoLine>? shown = null;
        var page = Open();
        page.Render(p => p.Add(x => x.Totals, (RenderFragment<IReadOnlyList<SamplePoLine>>)(lines => _ => shown = lines)));
        var lines = Lines(page);

        lines.Edit(0, "qty", "1");
        lines.Select(1);
        page.Find("[data-testid=t-toolbar-mark-delete]").Click();

        shown.Should().ContainSingle().Which.Amount.Should().Be(850.00m);
    }
}

using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Row states I/U/D and the batch changes of a grid (TASK-CORE-006, screens.md "M").</summary>
public class GridRowsTests
{
    private static GridRows<SampleItem> Loaded()
    {
        var rows = new GridRows<SampleItem>();
        rows.Load([new SampleItem { Id = 1, Code = "A" }, new SampleItem { Id = 2, Code = "B" }]);
        return rows;
    }

    [Fact]
    public void Load_RowsAreUnchanged()
    {
        var rows = Loaded();

        rows.All.Should().OnlyContain(r => r.State == RowState.Unchanged);
        rows.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void EditLoadedRow_IsModified_AndBackToUnchangedWhenRestored()
    {
        var rows = Loaded();

        rows.All[0].Item.Code = "X";
        rows.All[0].State.Should().Be(RowState.Modified);
        rows.IsDirty.Should().BeTrue();

        rows.All[0].Item.Code = "A";
        rows.All[0].State.Should().Be(RowState.Unchanged);
    }

    [Fact]
    public void Add_IsAdded()
    {
        var rows = Loaded();

        var row = rows.Add(new SampleItem { Code = "N" });

        row.State.Should().Be(RowState.Added);
        rows.All.Should().HaveCount(3);
    }

    [Fact]
    public void ToggleDelete_LoadedRow_IsDeleted_SecondToggleRestores()
    {
        var rows = Loaded();

        rows.ToggleDelete(rows.All[1]);
        rows.All[1].State.Should().Be(RowState.Deleted);

        rows.ToggleDelete(rows.All[1]);
        rows.All[1].State.Should().Be(RowState.Unchanged);
    }

    [Fact]
    public void ToggleDelete_NewRow_IsRemoved()
    {
        var rows = Loaded();
        var row = rows.Add(new SampleItem());

        rows.ToggleDelete(row);

        rows.All.Should().HaveCount(2);
        rows.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Changes_ToRequest_SendsAddedUpdatedAndDeletedIds()
    {
        var rows = Loaded();
        rows.Add(new SampleItem { Code = "N" });
        rows.All[0].Item.Code = "X";
        rows.ToggleDelete(rows.All[1]);

        var request = rows.Changes().ToRequest(r => r.Id);

        request.Added.Should().ContainSingle(r => r.Code == "N");
        request.Updated.Should().ContainSingle(r => r.Code == "X");
        request.Deleted.Should().Equal(2L);
    }

    [Fact]
    public void Changes_ModifiedThenDeleted_IsOnlyDeleted()
    {
        var rows = Loaded();
        rows.All[0].Item.Code = "X";
        rows.ToggleDelete(rows.All[0]);

        var changes = rows.Changes();

        changes.Updated.Should().BeEmpty();
        changes.Deleted.Should().ContainSingle();
    }
}

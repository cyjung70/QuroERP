using FluentAssertions;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Web.Components.Pages.Bc.Models;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Set item composition grid rows and their batch request (BC-UC-013).</summary>
public class SetComponentRowTests
{
    private static readonly DateTimeOffset Read = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly SetComponentDto Bolt = new(11, 1, 101, "BOLT", "Bolt", "EA", 2, Read);

    [Fact, Trait("Id", "SD-UT-508")]
    public void CopyOf_IsANewRowWithTheComponentAndQty_WithoutIdOrUpdatedAt()
    {
        var row = SetComponentRow.CopyOf(Bolt);

        row.Should().BeEquivalentTo(new SetComponentRow { Id = 0, ItemCd = "BOLT", ItemName = "Bolt", Qty = 2, UpdatedAt = null });
    }

    [Fact, Trait("Id", "SD-UT-506")]
    public void ToRequest_SendsAddedUpdatedAndDeletedRows_ABlankQtyAsZero()
    {
        var loaded = SetComponentRow.From(Bolt);
        loaded.Qty = 3;
        var added = new SetComponentRow { ItemCd = "NUT" };
        var deleted = SetComponentRow.From(Bolt with { Id = 12, ItemCd = "WASHER" });

        var request = SetComponentRow.ToRequest(new BatchChanges<SetComponentRow>([added], [loaded], [deleted]));

        request.Added.Should().Equal(new SetComponentRowRequest(null, "NUT", 0, null));
        request.Updated.Should().Equal(new SetComponentRowRequest(11, "BOLT", 3, Read));
        request.Deleted.Should().Equal(new SetComponentDeleteRequest(12, Read));
    }
}

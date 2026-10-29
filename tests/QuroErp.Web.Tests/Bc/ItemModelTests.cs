using FluentAssertions;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Web.Components.Pages.Bc.Models;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Item screen model: defaults (MM-BR-802, 803) and mapping to the API requests.</summary>
public class ItemModelTests
{
    [Fact, Trait("Id", "BC-009-WT-06")]
    public void New_HasScreenDefaults()
    {
        var item = new ItemModel();

        item.ItemType.Should().Be("INVENTORY");
        item.Unit.Should().Be("EA");
        item.ValidFlag.Should().Be("Y");
        item.UsdPrc.Should().Be(0.01m);
        item.ItemGrade.Should().BeNull();
        item.Taxable.Should().BeTrue();
        item.VatKind.Should().Be("TAXABLE");
    }

    [Fact, Trait("Id", "BC-009-WT-07")]
    public void FromDto_ToUpdateRequest_KeepsFieldsAndUpdatedAt_WithoutReadOnlyValues()
    {
        var updatedAt = new DateTimeOffset(2026, 10, 29, 1, 2, 3, TimeSpan.Zero);
        var dto = new ItemDto(7, "A100", "APPLE", "RED", "DESC", "SET", "BX", "FR", "N", 1.5m, 2m, "123", "L1", 3m,
            "M1", "M2", "M3", "M4", "M5", "A01", false, "EXEMPT", "A10", 9m, 8m, 7m, updatedAt);

        var request = ItemModel.From(dto).ToUpdateRequest();

        request.Should().BeEquivalentTo(new UpdateItemRequest
        {
            ItemName = "APPLE", ItemName2 = "RED", ItemDesc = "DESC", ItemType = "SET", Unit = "BX", ItemClass = "FR",
            ValidFlag = "N", UsdPrc = 1.5m, SoUsdPrc = 2m, UpcCode = "123", Location = "L1", ReorderPoint = 3m,
            Memo1 = "M1", Memo2 = "M2", Memo3 = "M3", Memo4 = "M4", Memo5 = "M5", ItemGrade = "A01",
            Taxable = false, VatKind = "EXEMPT", UpdatedAt = updatedAt,
        });
    }

    [Fact, Trait("Id", "BC-009-WT-08")]
    public void ToCreateRequest_SendsItemCode()
    {
        new ItemModel { ItemCd = "B200" }.ToCreateRequest().ItemCd.Should().Be("B200");
    }
}

using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Items;

public class ItemTests
{
    private static readonly ItemFields Valid = new(
        "BOLT", null, null, null, null, "PART", null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static Item Stored(ItemFields? fields = null, string? plating = Item.DefaultPlating) =>
        Item.Restore(5, "B100", fields ?? Valid with { ItemType = "INVENTORY", Unit = "EA", ValidFlag = "Y", UsdPrc = 2m }, plating, 1.5m, 10, 15);

    private static DomainException Error(Action act) => act.Should().Throw<DomainException>().Which;

    [Theory, Trait("Id", "MM-UT-801")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("b100")]
    [InlineData("B 100")]
    [InlineData("ABCDEFGHIJ123456789")]
    public void Create_InvalidCode_RejectsMmE801(string? itemCd)
    {
        Error(() => Item.Create(itemCd, Valid)).Code.Should().Be(Item.InvalidCodeCode);
    }

    [Fact, Trait("Id", "MM-UT-801")]
    public void Create_EighteenCharacterCode_IsTrimmedAndKept()
    {
        Item.Create(" ABCDEFGHIJ12345678 ", Valid).ItemCd.Should().Be("ABCDEFGHIJ12345678");
    }

    [Fact, Trait("Id", "MM-UT-802")]
    public void Create_FillsDefaults_AndBlankTextIsNull()
    {
        var item = Item.Create("B100", Valid with { Memo1 = "  " });

        item.Fields.ItemType.Should().Be("INVENTORY");
        item.Fields.Unit.Should().Be("EA");
        item.Fields.ValidFlag.Should().Be("Y");
        item.Fields.ItemGrade.Should().BeNull();
        item.Fields.Memo1.Should().BeNull();
    }

    [Theory, Trait("Id", "MM-UT-802")]
    [InlineData(nameof(ItemFields.ItemName))]
    [InlineData(nameof(ItemFields.ItemClass))]
    public void Create_MissingNameOrClass_RejectsMmE802(string field)
    {
        var fields = field == nameof(ItemFields.ItemName) ? Valid with { ItemName = "" } : Valid with { ItemClass = null };

        var error = Error(() => Item.Create("B100", fields));

        error.Code.Should().Be(Item.RequiredCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "MM-UT-802")]
    public void Change_MissingUnit_RejectsMmE802()
    {
        var item = Stored();

        Error(() => item.Change(item.Fields with { Unit = null }, typeLocked: false)).Args.Should().Equal(nameof(ItemFields.Unit));
    }

    [Theory, Trait("Id", "MM-UT-803")]
    [InlineData(nameof(ItemFields.UsdPrc))]
    [InlineData(nameof(ItemFields.SoUsdPrc))]
    [InlineData(nameof(ItemFields.ReorderPoint))]
    public void Create_NegativeNumber_RejectsMmE803(string field)
    {
        var fields = field switch
        {
            nameof(ItemFields.UsdPrc) => Valid with { UsdPrc = -0.01m },
            nameof(ItemFields.SoUsdPrc) => Valid with { SoUsdPrc = -1 },
            _ => Valid with { ReorderPoint = -1 },
        };

        var error = Error(() => Item.Create("B100", fields));

        error.Code.Should().Be(Item.NegativeCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "MM-UT-803")]
    public void Create_NoPurchasePrice_DefaultsTo001()
    {
        Item.Create("B100", Valid).Fields.UsdPrc.Should().Be(0.01m);
    }

    [Fact, Trait("Id", "MM-UT-804")]
    public void Create_MovingAverageCostIsPurchasePrice_StockIsZero()
    {
        var item = Item.Create("B100", Valid with { UsdPrc = 3.25m });

        item.MvgCost.Should().Be(3.25m);
        item.OnhandQty.Should().Be(0);
        item.StkVal.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-805")]
    public void Change_PurchasePrice_KeepsMovingAverageCostAndStock()
    {
        var item = Stored();

        item.Change(item.Fields with { UsdPrc = 9m }, typeLocked: false);

        item.Fields.UsdPrc.Should().Be(9m);
        item.MvgCost.Should().Be(1.5m);
        item.OnhandQty.Should().Be(10);
        item.StkVal.Should().Be(15);
    }

    [Fact, Trait("Id", "MM-UT-806")]
    public void Change_TypeOfLockedItem_RejectsMmE806()
    {
        var item = Stored();

        var error = Error(() => item.Change(item.Fields with { ItemType = "SERVICE" }, typeLocked: true));

        error.Code.Should().Be(Item.TypeLockedCode);
        error.Args.Should().Equal("B100");
    }

    [Fact, Trait("Id", "MM-UT-806")]
    public void Change_OtherValuesOfLockedItem_OrTypeOfFreeItem_Saves()
    {
        var locked = Stored();
        var free = Stored();

        locked.Change(locked.Fields with { ItemName = "NUT" }, typeLocked: true);
        free.Change(free.Fields with { ItemType = "SERVICE" }, typeLocked: false);

        locked.Fields.ItemName.Should().Be("NUT");
        free.Fields.ItemType.Should().Be("SERVICE");
    }

    [Fact, Trait("Id", "MM-UT-807")]
    public void EnsureDeletable_InUse_RejectsMmE807()
    {
        Error(() => Stored().EnsureDeletable(inUse: true)).Code.Should().Be(Item.InUseCode);
        Stored().Invoking(i => i.EnsureDeletable(inUse: false)).Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-808")]
    [InlineData("R", "B100")]
    [InlineData(null, "B100")]
    [InlineData("", "B100")]
    [InlineData("G", "B10")]
    public void ItemGroupCd_IsCodeOrCodeWithoutLastLetter(string? plating, string expected)
    {
        Stored(plating: plating).ItemGroupCd.Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-808")]
    public void Create_PlatingIsR_GroupIsItemCode()
    {
        Item.Create("B100", Valid).ItemGroupCd.Should().Be("B100");
    }

    [Fact, Trait("Id", "MM-UT-809")]
    public void EnsureSelectable_NotInUse_RejectsMmE809()
    {
        var notInUse = Stored(Valid with { ItemType = "INVENTORY", Unit = "EA", ValidFlag = "N", UsdPrc = 1 });

        Error(notInUse.EnsureSelectable).Code.Should().Be(Item.NotInUseCode);
        Stored().Invoking(i => i.EnsureSelectable()).Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-825")]
    [InlineData(nameof(ItemFields.ItemName), 100)]
    [InlineData(nameof(ItemFields.Location), 15)]
    [InlineData(nameof(ItemFields.Memo5), 50)]
    public void Create_TooLongText_RejectsMmE825(string field, int max)
    {
        var text = new string('A', max + 1);
        var fields = field switch
        {
            nameof(ItemFields.ItemName) => Valid with { ItemName = text },
            nameof(ItemFields.Location) => Valid with { Location = text },
            _ => Valid with { Memo5 = text },
        };

        var error = Error(() => Item.Create("B100", fields));

        error.Code.Should().Be(Item.TooLongCode);
        error.Args.Should().Equal(field, max);
    }

    [Fact, Trait("Id", "MM-UT-825")]
    public void Create_NameOf100Characters_Saves()
    {
        Item.Create("B100", Valid with { ItemName = new string('A', 100) }).Fields.ItemName.Should().HaveLength(100);
    }

    [Fact, Trait("Id", "MM-UT-831")]
    public void Create_TaxDefaults_TaxableAndVatTaxable()
    {
        var item = Item.Create("B100", Valid);

        item.Fields.Taxable.Should().BeTrue();
        item.Fields.VatKind.Should().Be(Item.VatTaxable);
        Item.Create("B101", Valid with { Taxable = false, VatKind = Item.VatExempt }).Fields.VatKind.Should().Be(Item.VatExempt);
    }

    [Fact, Trait("Id", "MM-UT-831")]
    public void Create_UnknownVatKind_RejectsMmE831()
    {
        Error(() => Item.Create("B100", Valid with { VatKind = "ZERO" })).Code.Should().Be(Item.InvalidVatKindCode);
    }

    [Theory, Trait("Id", "MM-UT-832")]
    [InlineData(10, 1, 2, RoundingMode.HalfUp, 10)]
    [InlineData(10, 1350.5, 0, RoundingMode.HalfUp, 13505)]
    [InlineData(1.2345, 0.00074, 4, RoundingMode.HalfUp, 0.0009)]
    [InlineData(1.2345, 0.00074, 4, RoundingMode.Down, 0.0009)]
    [InlineData(0.15, 3, 1, RoundingMode.HalfUp, 0.5)]
    [InlineData(0.15, 3, 1, RoundingMode.Down, 0.4)]
    public void DocumentUnitPrice_IsStandardPriceTimesRateRounded(
        double standard, double rate, int decimals, RoundingMode mode, double expected)
    {
        Item.DocumentUnitPrice((decimal)standard, (decimal)rate, decimals, mode).Should().Be((decimal)expected);
    }
}

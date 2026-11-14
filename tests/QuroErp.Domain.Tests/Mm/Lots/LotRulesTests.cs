using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Tests.Mm.Lots;

/// <summary>Lot rules and item lot management (MM lot.md, MM-BR-351~356, 361). Lot balances: StockKeyRulesTests.</summary>
public class LotRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    private static readonly ItemFields Fields = new(
        "BOLT", null, null, "INVENTORY", "EA", "PART", "Y", 2m, null, null, null, null, null, null, null, null, null, null, null, null);

    private static DomainException Error(Action act) => act.Should().Throw<DomainException>().Which;

    [Theory, Trait("Id", "MM-UT-352"), Trait("Rule", "MM-BR-352")]
    [InlineData(" b-1_x ", "B-1_X")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234567890")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeNumber_TrimsAndUpperCases_EmptyMeansAutomatic(string? lotNo, string? expected)
    {
        LotRules.NormalizeNumber("A", lotNo).Should().Be(expected);
    }

    [Theory, Trait("Id", "MM-UT-352"), Trait("Rule", "MM-BR-352")]
    [InlineData("A B")]
    [InlineData("LOT#1")]
    [InlineData("1234567890123456789012345678901")]
    [InlineData("*")]
    public void NormalizeNumber_AgainstTheRules_IsMmE352(string lotNo)
    {
        var error = Error(() => LotRules.NormalizeNumber("A", lotNo));

        error.Code.Should().Be(LotRules.InvalidNumberCode);
        error.Args[0].Should().Be("A");
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353")]
    public void AutoNumber_IsTheDocumentDateAndTheNextNumber_PastNineHundredNinetyNineWithMoreDigits()
    {
        LotRules.AutoNumberPrefix(Day).Should().Be("261010-");
        LotRules.AutoNumber(Day, 0).Should().Be("261010-001");
        LotRules.AutoNumber(Day, 41).Should().Be("261010-042");
        LotRules.AutoNumber(Day, 999).Should().Be("261010-1000");
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public void EnsureManagedFrom_BlocksALotOtherThanStarBeforeLotManagementStarted()
    {
        FluentActions.Invoking(() => LotRules.EnsureManagedFrom(Day, Day, "A", "MAIN", "*", "B", 3)).Should().NotThrow();
        FluentActions.Invoking(() => LotRules.EnsureManagedFrom(Day, null, "A", "MAIN", "*", "B", 3)).Should().NotThrow();
        FluentActions.Invoking(() => LotRules.EnsureManagedFrom(Day.AddDays(-1), Day, "A", "MAIN", "*", "*", 3)).Should().NotThrow();

        Error(() => LotRules.EnsureManagedFrom(Day.AddDays(-1), Day, "A", "MAIN", "A01", "B", 3))
            .Args.Should().Equal("A", "MAIN", "A01", "B", "2026-10-09", 3m);
    }

    [Fact, Trait("Id", "MM-UT-351"), Trait("Rule", "MM-BR-351")]
    public void Item_TurningLotsOnAndOff_SetsAndClearsTheStartDay()
    {
        var item = Item.Restore(5, "B100", Fields, null, 1, 10, 10);

        item.Change(Fields with { LotManaged = true }, typeLocked: false);
        item.StartLotManagement(Day);
        (item.LotManaged, item.LotManagedFrom).Should().Be((true, Day));

        // A missing setting keeps it
        item.Change(Fields, typeLocked: false);
        item.LotManaged.Should().BeTrue();

        item.Change(Fields with { LotManaged = false }, typeLocked: false);
        item.StopLotManagement(lotStockLeft: false);
        (item.LotManaged, item.LotManagedFrom).Should().Be((false, (DateOnly?)null));
    }

    [Fact, Trait("Id", "MM-UT-351"), Trait("Rule", "MM-BR-351")]
    public void Item_LotsOnlyForItemsWithOwnStock_AndOffOnlyWithoutLotStock()
    {
        var item = Item.Restore(5, "B100", Fields with { LotManaged = true }, null, 1, 10, 10, Day);

        Error(() => Item.Create("S1", Fields with { ItemType = "SERVICE", LotManaged = true })).Args.Should().Equal("S1");
        Error(() => item.Change(Fields with { ItemType = "KIT" }, typeLocked: false)).Code.Should().Be(LotRules.ManagementCode);
        Error(() => item.StopLotManagement(lotStockLeft: true)).Code.Should().Be(LotRules.ManagementCode);
        // The stock stays on lot '*', so a warehouse below zero does not matter (MM-BR-351)
        FluentActions.Invoking(() => Item.Restore(6, "B200", Fields, null, 1, -10, -10).StartLotManagement(Day)).Should().NotThrow();
        // A Set has its own stock, so it can be lot-managed too
        FluentActions.Invoking(() => Item.Create("S2", Fields with { ItemType = "SET", LotManaged = true })).Should().NotThrow();
        Item.Create("N1", Fields).LotManaged.Should().BeFalse();
    }

    // MM-BR-361: a Kit or Set component can turn lot management on; nothing asks whether it is a component
    [Fact, Trait("Id", "MM-UT-361"), Trait("Rule", "MM-BR-361")]
    public void Item_AnInventoryItem_CanTurnLotsOn_ComponentOrNot()
    {
        var item = Item.Restore(5, "B100", Fields, null, 1, 10, 10);

        item.StartLotManagement(Day);

        item.LotManagedFrom.Should().Be(Day);
    }
}

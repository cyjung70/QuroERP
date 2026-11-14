using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Stock key item + warehouse + Location + Lot (MM-BR-713, MM-BR-714, MM-BR-718, MM-BR-356, MM-BR-363).</summary>
public class StockKeyRulesTests
{
    private const long Main = 1;
    private static readonly DateOnly Day = new(2026, 10, 10);

    private static readonly StockLocation Star = new(10, Main, "*", true);
    private static readonly StockLocation Shelf = new(11, Main, "A01", true);

    private static DomainException Error(Action act) => act.Should().Throw<DomainException>().Which;

    [Fact, Trait("Id", "MM-UT-713"), Trait("Rule", "MM-BR-713")]
    public void EnsureLocation_StarForEveryItem_OtherLocationsOnlyForLocationManagedItemsInUseInTheLineWarehouse()
    {
        FluentActions.Invoking(() => StockKeyRules.EnsureLocation("A", false, Main, "MAIN", Star, "*")).Should().NotThrow();
        FluentActions.Invoking(() => StockKeyRules.EnsureLocation("A", true, Main, "MAIN", Shelf, "11")).Should().NotThrow();

        var notManaged = Error(() => StockKeyRules.EnsureLocation("A", false, Main, "MAIN", Shelf, "11"));
        notManaged.Code.Should().Be(StockKeyRules.LocationCode);
        notManaged.Args.Should().Equal("A", "MAIN", "A01");
        Error(() => StockKeyRules.EnsureLocation("A", true, Main, "MAIN", Shelf with { InUse = false }, "11")).Args.Should().Equal("A", "MAIN", "A01");
        Error(() => StockKeyRules.EnsureLocation("A", true, Main, "MAIN", Shelf with { WarehouseId = 2 }, "11")).Args.Should().Equal("A", "MAIN", "A01");
        Error(() => StockKeyRules.EnsureLocation("A", true, Main, "MAIN", null, "99")).Args.Should().Equal("A", "MAIN", "99");
    }

    [Fact, Trait("Id", "MM-UT-363"), Trait("Rule", "MM-BR-363")]
    public void EnsureLot_ItemWithoutLotManagement_UsesOnlyStar()
    {
        FluentActions.Invoking(() => StockKeyRules.EnsureLot("A", false, "*")).Should().NotThrow();
        FluentActions.Invoking(() => StockKeyRules.EnsureLot("A", true, "B")).Should().NotThrow();

        var error = Error(() => StockKeyRules.EnsureLot("A", false, "B"));

        error.Code.Should().Be(StockKeyRules.LotCode);
        error.Args.Should().Equal("A", "B");
    }

    [Theory, Trait("Id", "MM-UT-714"), Trait("Rule", "MM-BR-714")]
    [InlineData(NegativeStockMode.AllowInMonth, "*", "*", false)]
    [InlineData(NegativeStockMode.Deny, "*", "*", true)]
    [InlineData(NegativeStockMode.AllowInMonth, "A01", "*", true)]
    [InlineData(NegativeStockMode.AllowInMonth, "*", "B", true)]
    public void MustStayNonNegative_OnlyStarStarFollowsTheMode(NegativeStockMode mode, string locationCode, string lotNo, bool expected)
    {
        StockKeyRules.MustStayNonNegative(mode, locationCode, lotNo).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public void EnsureBalance_ChecksTheDocumentDateAndEveryLaterDay()
    {
        // Lot balance now 6: +5 on 10/12, −3 on 10/15. End of 10/10 = 4, 10/12 = 9, 10/15 = 6
        StockKeyDailyQty[] daily = [new(new(2026, 10, 12), 5), new(new(2026, 10, 15), -3)];

        FluentActions.Invoking(() => StockKeyRules.EnsureBalance(Day, -4, 6, daily, "A", "MAIN", "*", "B")).Should().NotThrow();
        var error = Error(() => StockKeyRules.EnsureBalance(Day, -5, 6, daily, "A", "MAIN", "*", "B"));

        error.Code.Should().Be(LotRules.NegativeCode);
        error.Args.Should().Equal("A", "MAIN", "*", "B", "2026-10-10", -1m);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public void EnsureBalance_ALaterIssue_LimitsAnEarlierOne()
    {
        // Now 1: the lot had 5 until 10/15, when 4 went out
        StockKeyDailyQty[] daily = [new(new(2026, 10, 15), -4)];

        Error(() => StockKeyRules.EnsureBalance(Day, -2, 1, daily, "A", "MAIN", "*", "B")).Args.Should().Equal("A", "MAIN", "*", "B", "2026-10-15", -1m);
        FluentActions.Invoking(() => StockKeyRules.EnsureBalance(Day, 3, 1, daily, "A", "MAIN", "*", "B")).Should().NotThrow();
    }

    [Fact, Trait("Id", "MM-UT-714"), Trait("Rule", "MM-BR-714")]
    public void EnsureBalance_LotStar_IsMmE714WithTheLocation()
    {
        var error = Error(() => StockKeyRules.EnsureBalance(Day, -3, 2, [], "A", "MAIN", "A01", "*"));

        error.Code.Should().Be(StockKeyRules.NegativeCode);
        error.Args.Should().Equal("A", "MAIN", "A01", "*", -1m);
    }

    [Fact, Trait("Id", "MM-UT-718"), Trait("Rule", "MM-BR-718")]
    public void Item_LocationManagement_TurnsOnFreely_AndOffOnlyWithoutStockOnOtherLocations()
    {
        ItemFields fields = new(
            "BOLT", null, null, "INVENTORY", "EA", "PART", "Y", 2m, null, null, null, null, null, null, null, null, null, null, null, null);
        var item = Item.Restore(5, "B100", fields, null, 1, 10, 10);

        item.Change(fields with { LocationManaged = true }, typeLocked: false);
        item.LocationManaged.Should().BeTrue();
        item.Change(fields, typeLocked: false);
        item.LocationManaged.Should().BeTrue("a missing setting keeps it");

        item.Change(fields with { LocationManaged = false }, typeLocked: false);
        var error = Error(() => item.StopLocationManagement(locationStockLeft: true));
        error.Code.Should().Be(StockKeyRules.LocationManagementCode);
        error.Args.Should().Equal("B100");
        FluentActions.Invoking(() => item.StopLocationManagement(locationStockLeft: false)).Should().NotThrow();
        Item.Create("N1", fields).LocationManaged.Should().BeFalse();
    }
}

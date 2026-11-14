using FluentAssertions;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>FIFO suggestion of the Locations and Lots of a decrease (MM-BR-716, MM-BR-362).</summary>
public class StockSuggestionTests
{
    private static readonly DateOnly Early = new(2026, 9, 1);
    private static readonly DateOnly Late = new(2026, 10, 1);

    [Fact, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    public void FifoOrder_StarFirst_ThenReceivedDate_LotNumber_LocationCode_AndOnlyBalancesAboveZero()
    {
        StockBalance[] balances =
        [
            new(3, "B01", 21, "260901-002", Early, 1),
            new(2, "A01", 22, "261001-001", Late, 1),
            new(1, "*", 20, "*", null, 1),
            new(2, "A01", 21, "260901-002", Early, 1),
            new(2, "A01", 23, "260901-001", Early, 1),
            new(1, "*", 24, "260801-001", Early.AddMonths(-1), 0),
        ];

        StockSuggestion.FifoOrder(balances).Select(b => (b.LotNo, b.LocationCode)).Should().Equal(
            ("*", "*"), ("260901-001", "A01"), ("260901-002", "A01"), ("260901-002", "B01"), ("261001-001", "A01"));
    }

    [Fact, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    public void Fifo_SplitsOverSeveralLotsAndLocations_UntilTheQuantityIsFilled()
    {
        StockBalance[] balances =
        [
            new(2, "A01", 22, "261001-001", Late, 10),
            new(1, "*", 20, "*", null, 2),
            new(2, "A01", 21, "260901-001", Early, 3),
        ];

        var picks = StockSuggestion.Fifo(balances, 7);

        picks.Should().Equal(
            new StockPick(1, "*", 20, "*", 2),
            new StockPick(2, "A01", 21, "260901-001", 3),
            new StockPick(2, "A01", 22, "261001-001", 2));
    }

    [Fact, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    public void Fifo_ShortStock_SuggestsWhatThereIs()
    {
        StockBalance[] balances = [new(1, "*", 20, "*", null, 2), new(2, "A01", 21, "260901-001", Early, 0)];

        StockSuggestion.Fifo(balances, 5).Should().Equal(new StockPick(1, "*", 20, "*", 2));
        StockSuggestion.Fifo([], 5).Should().BeEmpty();
        FluentActions.Invoking(() => StockSuggestion.Fifo(balances, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(8, 2)]
    public void ShortQty_IsWhatThePicksDoNotCover_AndZeroWhenCovered(decimal qty, decimal expected)
    {
        StockBalance[] balances = [new(1, "*", 20, "*", null, 2), new(2, "A01", 21, "260901-001", Early, 4)];

        var picks = StockSuggestion.Fifo(balances, qty);

        StockSuggestion.ShortQty(picks, qty).Should().Be(expected);
        StockSuggestion.ShortQty([], 3).Should().Be(3);
    }
}

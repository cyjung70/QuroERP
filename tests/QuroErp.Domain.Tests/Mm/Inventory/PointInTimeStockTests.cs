using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Point-in-time (MM-BR-009) and month-end (MM-BR-010) negative stock checks on daily balances (MM-BR-604).</summary>
public class PointInTimeStockTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static DailyStockBalance B(int month, int day, decimal balance, bool monthEnd = false) => new(D(month, day), balance, monthEnd);

    private static bool Check(NegativeStockMode mode, DateOnly date, decimal qty, decimal opening, params DailyStockBalance[] balances) =>
        PointInTimeStock.Check(mode, date, qty, opening, balances, "A", "MAIN");

    [Fact, Trait("Id", "MM-UT-009")]
    public void Deny_BalanceOnTheDateGoesNegative_ThrowsWithThatDate()
    {
        // Last movement before 10/5 is 10/1 with 2 → 10/5 has 2
        var act = () => Check(NegativeStockMode.Deny, D(10, 5), -3, 0, B(10, 1, 2), B(10, 20, 10, monthEnd: true));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-009");
        error.Args.Should().Equal("A", "MAIN", "2026-10-05", -1m, "");
    }

    [Fact, Trait("Id", "MM-UT-009")]
    public void Deny_ALaterDayGoesNegative_ThrowsWithTheFirstNegativeDay()
    {
        var act = () => Check(NegativeStockMode.Deny, D(10, 5), -3, 0,
            B(10, 1, 10), B(10, 8, 4), B(10, 9, 2), B(11, 2, 1, monthEnd: true));

        var error = act.Should().Throw<DomainException>().Which;
        error.Args.Should().Equal("A", "MAIN", "2026-10-09", -1m, "");
    }

    [Fact, Trait("Id", "MM-UT-009")]
    public void Deny_NoMovementsYet_UsesTheOpeningBalance()
    {
        Check(NegativeStockMode.Deny, D(10, 5), -3, opening: 3).Should().BeFalse();

        var act = () => Check(NegativeStockMode.Deny, D(10, 5), -4, opening: 3);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-009");
    }

    [Fact, Trait("Id", "MM-UT-009")]
    public void Deny_AllBalancesStayAtOrAboveZero_Passes()
    {
        Check(NegativeStockMode.Deny, D(10, 5), -3, 0, B(10, 5, 3), B(10, 9, 5, monthEnd: true)).Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public void AllowInMonth_OnlyMidMonthNegative_PassesAndFlags()
    {
        Check(NegativeStockMode.AllowInMonth, D(10, 5), -3, 0, B(10, 1, 1), B(10, 20, 6, monthEnd: true)).Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public void AllowInMonth_MonthWithoutMovementOnTheLastDay_UsesItsLastMovementDay()
    {
        // October ends on 10/20 (no movement on 10/31)
        var act = () => Check(NegativeStockMode.AllowInMonth, D(10, 5), -3, 0,
            B(10, 1, 5), B(10, 20, 2, monthEnd: true), B(11, 3, 10, monthEnd: true));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-010");
        error.Args.Should().Equal("A", "MAIN", "202610", -1m, "");
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public void AllowInMonth_NoLaterMovementInTheMovementMonth_ChecksTheDateBalance()
    {
        // The current month ends with the new movement itself
        var act = () => Check(NegativeStockMode.AllowInMonth, D(10, 5), -3, 0, B(10, 1, 2), B(11, 3, 10, monthEnd: true));

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("A", "MAIN", "202610", -1m, "");
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public void AllowInMonth_FutureMonthEndGoesNegative_Throws()
    {
        var act = () => Check(NegativeStockMode.AllowInMonth, D(10, 5), -3, 0,
            B(10, 1, 10), B(10, 31, 10, monthEnd: true), B(11, 15, 2, monthEnd: true), B(12, 1, 20, monthEnd: true));

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("A", "MAIN", "202611", -1m, "");
    }

    [Theory, Trait("Id", "MM-UT-602")]
    [InlineData(NegativeStockMode.Deny)]
    [InlineData(NegativeStockMode.AllowInMonth)]
    public void IncreasingMovement_IsNotChecked(NegativeStockMode mode)
    {
        Check(mode, D(10, 5), 3, -100, B(10, 1, -50)).Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UT-002")]
    public void Allow_IsNotChecked()
    {
        Check(NegativeStockMode.Allow, D(10, 5), -3, 0, B(10, 1, 0, monthEnd: true)).Should().BeFalse();
    }
}

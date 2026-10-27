using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Movements in a closed month are blocked (MM-BR-004).</summary>
public class InventoryClosingTests
{
    [Theory, Trait("Id", "MM-UT-004")]
    [InlineData("202610", "2026-09-30")]
    [InlineData("202601", "2025-12-31")]
    [InlineData("202603", "2026-02-28")]
    public void LastClosedMonthEnd_IsTheDayBeforeTheFirstOpenMonth(string closeYearMonth, string expected)
    {
        InventoryClosing.LastClosedMonthEnd(closeYearMonth).Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact, Trait("Id", "MM-UT-004")]
    public void EnsureOpen_NoClosedMonth_AllowsAnyDate()
    {
        var act = () => InventoryClosing.EnsureOpen(null, new DateOnly(2001, 1, 1));

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "MM-UT-004")]
    public void EnsureOpen_DateInClosedMonth_ThrowsMmE002()
    {
        var act = () => InventoryClosing.EnsureOpen("202610", new DateOnly(2026, 9, 30));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-002");
        error.Args.Should().Equal("2026-09-30", "202609");
    }

    [Fact, Trait("Id", "MM-UT-004")]
    public void EnsureOpen_FirstDayOfOpenMonth_IsAllowed()
    {
        var act = () => InventoryClosing.EnsureOpen("202610", new DateOnly(2026, 10, 1));

        act.Should().NotThrow();
    }
}

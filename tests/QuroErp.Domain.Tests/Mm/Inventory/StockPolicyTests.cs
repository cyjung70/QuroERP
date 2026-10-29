using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>On-hand negative stock check per item + warehouse (MM-BR-001, 002, 003, 602).</summary>
public class StockPolicyTests
{
    [Fact, Trait("Id", "MM-UT-001")]
    public void CheckOnHand_DenyAndIssueAboveOnHand_ThrowsWithItemWarehouseOnHandAndRequest()
    {
        var act = () => StockPolicy.CheckOnHand(NegativeStockMode.Deny, onHand: 5, qty: -7, "ITEM-1", "MAIN");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal("ITEM-1", "MAIN", 5m, 7m, "");
    }

    [Fact, Trait("Id", "MM-UT-001")]
    public void CheckOnHand_DenyAndIssueEqualToOnHand_IsAllowedWithoutFlag()
    {
        StockPolicy.CheckOnHand(NegativeStockMode.Deny, onHand: 5, qty: -5, "ITEM-1", "MAIN").Should().BeFalse();
    }

    [Theory, Trait("Id", "MM-UT-002")]
    [InlineData(NegativeStockMode.Allow)]
    [InlineData(NegativeStockMode.AllowInMonth)]
    public void CheckOnHand_AllowModesAndIssueAboveOnHand_ReturnsNegativeFlag(NegativeStockMode mode)
    {
        StockPolicy.CheckOnHand(mode, onHand: 5, qty: -7, "ITEM-1", "MAIN").Should().BeTrue();
    }

    [Theory, Trait("Id", "MM-UT-003")]
    [InlineData(null, NegativeStockMode.Deny)]
    [InlineData("ALLOW", NegativeStockMode.Allow)]
    [InlineData("ALLOW_IN_MONTH", NegativeStockMode.AllowInMonth)]
    [InlineData("DENY", NegativeStockMode.Deny)]
    public void ModeOrDefault_MissingRowIsDeny(string? code, NegativeStockMode expected)
    {
        StockPolicy.ModeOrDefault(code).Should().Be(expected);
    }

    [Theory, Trait("Id", "MM-UT-602")]
    [InlineData(-3, 2)]
    [InlineData(-3, 5)]
    [InlineData(0, 1)]
    public void CheckOnHand_IncreasingMovement_IsNeverCheckedEvenWhenStockStaysNegative(decimal onHand, decimal qty)
    {
        StockPolicy.CheckOnHand(NegativeStockMode.Deny, onHand, qty, "ITEM-1", "MAIN").Should().BeFalse();
    }
}

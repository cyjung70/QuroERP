using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Sd.Assemblies;

namespace QuroErp.Domain.Tests.Sd.Assemblies;

/// <summary>Set assembly and disassembly rules (SD-BR-514~516, SD-BR-518).</summary>
public class AssemblyRulesTests
{
    private static readonly DateOnly Early = new(2026, 9, 1);
    private static readonly DateOnly Late = new(2026, 10, 1);

    [Fact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518")]
    public void Allocate_TakesTheLocationsAndLotsInFifoOrder_SplittingWhenOneIsNotEnough()
    {
        StockBalance[] balances =
        [
            new(2, "A01", 22, "261001-001", Late, 10),
            new(2, "A01", 21, "260901-001", Early, 3),
        ];

        var picks = AssemblyRules.Allocate("SET1", "C1", "MAIN", balances, 5);

        picks.Should().Equal(new StockPick(2, "A01", 21, "260901-001", 3), new StockPick(2, "A01", 22, "261001-001", 2));
    }

    [Fact, Trait("Id", "SD-UT-514"), Trait("Rule", "SD-BR-514")]
    public void Allocate_ShortStock_ThrowsSdE514WithTheNeededAndAvailableQty()
    {
        StockBalance[] balances = [new(1, "*", 20, "*", null, 2), new(2, "A01", 21, "260901-001", Early, 1)];

        var act = () => AssemblyRules.Allocate("SET1", "C1", "MAIN", balances, 5);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("SD-E-514", new object[] { "SET1", "C1", "MAIN", 5m, 3m }));
    }

    [Fact, Trait("Id", "SD-UT-514"), Trait("Rule", "SD-BR-514")]
    public void Allocate_NoStockAtAll_ThrowsSdE514WithZeroAvailable()
    {
        var act = () => AssemblyRules.Allocate("SET1", "C1", "MAIN", [], 2);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("SET1", "C1", "MAIN", 2m, 0m);
    }

    [Theory, Trait("Id", "SD-UT-515"), Trait("Rule", "SD-BR-515")]
    [InlineData(100, 3, 2, RoundingMode.HalfUp, 33.33)]
    [InlineData(200, 3, 2, RoundingMode.HalfUp, 66.67)]
    [InlineData(200, 3, 2, RoundingMode.Down, 66.66)]
    [InlineData(200, 3, 0, RoundingMode.HalfUp, 67)]
    [InlineData(90, 4, 4, RoundingMode.HalfUp, 22.5)]
    public void UnitPrice_IsTheIssueAmountOverTheSetQty_RoundedToThePriceDecimals(
        decimal amount, decimal qty, int decimals, RoundingMode mode, decimal expected)
    {
        AssemblyRules.UnitPrice(amount, qty, decimals, mode).Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-515"), Trait("Rule", "SD-BR-515")]
    public void SetReceipt_KeepsTheIssueAmount_NotQtyTimesTheRoundedPrice()
    {
        var price = AssemblyRules.UnitPrice(100m, 3m, 2, RoundingMode.HalfUp);

        var movement = GoodsMovement.Create(MovementType.SetAssembly, Early, 1, 2, 3m, price, "AS2609000001", amount: 100m);

        (movement.Prc, movement.Amount).Should().Be((33.33m, 100m));
    }

    [Fact, Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516")]
    public void EnsureCanDisassemble_AboveTheQtyLeft_ThrowsSdE516()
    {
        var act = () => AssemblyRules.EnsureCanDisassemble("AS2611000001", 3m, 2m);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("SD-E-516", new object[] { "AS2611000001", 2m }));
        FluentActions.Invoking(() => AssemblyRules.EnsureCanDisassemble("AS2611000001", 2m, 2m)).Should().NotThrow();
    }

    [Fact, Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516")]
    public void Return_FillsTheLastUsedLotFirst_AtItsPrice()
    {
        // 4 Sets used 2 per Set: 3 of Lot 21, then 5 of Lot 22
        AssemblyPart[] used = [new(1, 7, 21, 3m, 10m), new(2, 7, 22, 5m, 12m)];

        var parts = AssemblyRules.Return(used, 4m, 0m, 3m);

        parts.Should().Equal(new ReturnPart(2, 7, 22, 5m, 12m), new ReturnPart(1, 7, 21, 1m, 10m));
    }

    [Fact, Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516")]
    public void Return_SkipsWhatEarlierDisassembliesGaveBack_FromTheEnd()
    {
        AssemblyPart[] used = [new(1, 7, 21, 3m, 10m), new(2, 7, 22, 5m, 12m)];

        var second = AssemblyRules.Return(used, 4m, 4m, 1m);
        var last = AssemblyRules.Return(used, 4m, 6m, 1m);

        second.Should().Equal(new ReturnPart(2, 7, 22, 1m, 12m), new ReturnPart(1, 7, 21, 1m, 10m));
        last.Should().Equal(new ReturnPart(1, 7, 21, 2m, 10m));
    }

    [Theory, Trait("Id", "SD-UT-516")]
    [InlineData("ASSEMBLE", true)]
    [InlineData("DISASSEMBLE", true)]
    [InlineData("assemble", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKind_KnowsTheTwoKinds(string? kind, bool expected) => AssemblyRules.IsKind(kind).Should().Be(expected);
}

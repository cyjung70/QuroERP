using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Pallet split and merge (MM-BR-911, MM-BR-914, MM-BR-915).</summary>
public class PalletSplitMergeRulesTests
{
    private static readonly PalletState Target = new(1, "PL2611000001", 10, 20, 30, 40, 6m, PalletRules.Active);

    [Theory, Trait("Id", "MM-UT-914"), Trait("Rule", "MM-BR-914")]
    [InlineData(1, 5)]
    [InlineData(5.5, 0.5)]
    public void Split_APartBelowTheQty_LeavesTheRest(decimal qty, decimal rest)
    {
        PalletRules.Split(Target, qty).Should().Be(rest);
    }

    [Theory, Trait("Id", "MM-UT-914"), Trait("Rule", "MM-BR-914")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    public void Split_NothingOrTheWholeQtyOrMore_IsRefused(decimal qty)
    {
        var split = () => PalletRules.Split(Target, qty);

        var error = split.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo((PalletRules.SplitCode, new object[] { "PL2611000001", 6m }));
    }

    [Fact, Trait("Id", "MM-UT-911"), Trait("Id", "MM-UT-914"), Trait("Rule", "MM-BR-911")]
    public void Split_AClosedPallet_IsRefused()
    {
        var split = () => PalletRules.Split(Target with { Qty = 0m, Status = PalletRules.Closed }, 1m);

        split.Should().Throw<DomainException>().Which.Code.Should().Be(PalletRules.SplitCode);
    }

    [Fact, Trait("Id", "MM-UT-915"), Trait("Rule", "MM-BR-915")]
    public void Merge_ActivePalletsOfTheSameKey_GivesTheTargetTheTotal()
    {
        var total = PalletRules.Merge(Target, [Source(2, 4m), Source(3, 2.5m)]);

        total.Should().Be(12.5m);
    }

    public static TheoryData<string, PalletState> Mismatches => new()
    {
        { "item", Source(2, 1m) with { ItemId = 11 } },
        { "lot", Source(2, 1m) with { LotId = 21 } },
        { "warehouse", Source(2, 1m) with { WarehouseId = 31 } },
        { "location", Source(2, 1m) with { LocationId = 41 } },
        { "closed", Source(2, 0m) with { Status = PalletRules.Closed } },
        { "the target itself", Target },
    };

    [Theory, MemberData(nameof(Mismatches)), Trait("Id", "MM-UT-915"), Trait("Id", "MM-UT-911"), Trait("Rule", "MM-BR-915")]
    public void Merge_APalletOfAnotherKey_OrNotActive_OrTheTarget_IsRefusedWithItsNumber(string reason, PalletState source)
    {
        var merge = () => PalletRules.Merge(Target, [Source(5, 1m), source]);

        var error = merge.Should().Throw<DomainException>(reason).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((PalletRules.MergeCode, new object[] { source.PalletNo }));
    }

    [Fact, Trait("Id", "MM-UT-915"), Trait("Rule", "MM-BR-915")]
    public void Merge_ASourceNamedTwice_IsRefused()
    {
        var merge = () => PalletRules.Merge(Target, [Source(2, 1m), Source(2, 1m)]);

        merge.Should().Throw<DomainException>().Which.Args.Should().Equal("PL2611000002");
    }

    [Fact, Trait("Id", "MM-UT-915"), Trait("Rule", "MM-BR-915")]
    public void Merge_WithoutSources_OrIntoAClosedTarget_IsRefusedWithTheTarget()
    {
        var none = () => PalletRules.Merge(Target, []);
        var closed = () => PalletRules.Merge(Target with { Qty = 0m, Status = PalletRules.Closed }, [Source(2, 1m)]);

        none.Should().Throw<DomainException>().Which.Args.Should().Equal("PL2611000001");
        closed.Should().Throw<DomainException>().Which.Args.Should().Equal("PL2611000001");
    }

    private static PalletState Source(long id, decimal qty) => Target with { Id = id, PalletNo = $"PL261100000{id}", Qty = qty };
}

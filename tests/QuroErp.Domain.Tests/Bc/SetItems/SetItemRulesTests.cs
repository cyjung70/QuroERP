using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SetItems;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Tests.Bc.SetItems;

/// <summary>Set item composition rules (BC-UC-013, SD-BR-501~505, MM-BR-361).</summary>
public class SetItemRulesTests
{
    // SD quantities have 2 decimals
    private static readonly DecimalPolicy Decimals = new(
        "US", [new DecimalSetting("SD", 2, RoundingMode.HalfUp)], [new CountryDecimal("US", "SD", 2)], new Dictionary<string, string?>());

    private static readonly SetItemCandidate Set = new(1, "KIT", "Kit", "SET", "Y", false);
    private static readonly SetItemCandidate Part = new(2, "BOLT", "Bolt", "INVENTORY", "Y", false);

    private static DomainException ErrorOf(Action act) => act.Should().Throw<DomainException>().Which;

    [Fact, Trait("Id", "SD-UT-501"), Trait("Rule", "SD-BR-501")]
    public void Set_MustBeASetItemInUse()
    {
        SetItemRules.EnsureSet("KIT", Set).Should().Be(Set);

        foreach (var item in new SetItemCandidate?[] { null, Part, Set with { ValidFlag = "N" } })
        {
            var error = ErrorOf(() => SetItemRules.EnsureSet("KIT", item));
            (error.Code, error.Args).Should().BeEquivalentTo((SetItemRules.SetCode, new object[] { "KIT" }));
        }
    }

    [Fact, Trait("Id", "SD-UT-502"), Trait("Rule", "SD-BR-502")]
    public void Component_MustBeAnInventoryItemInUse_SoNoServiceAndNoSetInASet()
    {
        SetItemRules.EnsureComponent("BOLT", Part).Should().Be(Part);

        foreach (var item in new SetItemCandidate?[] { null, Set, Part with { ItemType = "SERVICE" }, Part with { ValidFlag = "N" } })
        {
            ErrorOf(() => SetItemRules.EnsureComponent("BOLT", item)).Code.Should().Be(SetItemRules.ComponentCode);
        }
    }

    [Fact, Trait("Id", "MM-UT-361"), Trait("Rule", "MM-BR-361")]
    public void Component_LotManaged_IsMmE361()
    {
        var error = ErrorOf(() => SetItemRules.EnsureComponent("BOLT", Part with { LotManaged = true }));

        (error.Code, error.Args).Should().BeEquivalentTo((LotRules.SetComponentCode, new object[] { "BOLT" }));
    }

    [Theory, Trait("Id", "SD-UT-503"), Trait("Rule", "SD-BR-503")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Qty_NotAboveZero_IsSdE503(decimal qty)
    {
        ErrorOf(() => SetItemRules.EnsureQty("BOLT", qty, Decimals)).Code.Should().Be(SetItemRules.QtyCode);
    }

    [Fact, Trait("Id", "SD-UT-503")]
    public void Qty_FollowsTheSdQuantityDecimals()
    {
        var act = () => SetItemRules.EnsureQty("BOLT", 1.25m, Decimals);
        act.Should().NotThrow();

        ErrorOf(() => SetItemRules.EnsureQty("BOLT", 1.255m, Decimals)).Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }

    [Fact, Trait("Id", "SD-UT-504"), Trait("Rule", "SD-BR-504")]
    public void Unique_EachComponentOnceInTheSet()
    {
        var act = () => SetItemRules.EnsureUnique(["A", "B"]);
        act.Should().NotThrow();

        var error = ErrorOf(() => SetItemRules.EnsureUnique(["A", "B", "A"]));
        (error.Code, error.Args).Should().BeEquivalentTo((SetItemRules.DuplicateCode, new object[] { "A" }));
    }

    [Fact, Trait("Id", "SD-UT-505"), Trait("Rule", "SD-BR-505")]
    public void NextSeqs_FollowTheLargestSequenceInOrder()
    {
        SetItemRules.NextSeqs(0, 2).Should().Equal(1, 2);
        SetItemRules.NextSeqs(7, 3).Should().Equal(8, 9, 10);
        SetItemRules.NextSeqs(7, 0).Should().BeEmpty();
    }

    [Theory, Trait("Id", "SD-UT-501")]
    [InlineData(" kit ", "KIT")]
    [InlineData("KIT", "KIT")]
    public void Code_IsTrimmedUpperCase_EmptyIsRequired(string input, string expected)
    {
        SetItemRules.NormalizeCode(input, "itemCd").Should().Be(expected);
        ErrorOf(() => SetItemRules.NormalizeCode(" ", "itemCd")).Code.Should().Be(SetItemRules.RequiredCode);
    }
}

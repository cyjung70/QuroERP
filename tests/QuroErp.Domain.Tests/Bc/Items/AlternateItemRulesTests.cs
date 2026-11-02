using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Items;

/// <summary>Alternate item group members (BC-UC-012, MM-BR-811~813).</summary>
public class AlternateItemRulesTests
{
    private const long Group = 1;
    private const long OtherGroup = 2;

    // 10 and 11 are members of group 1; 20 is free, 21 is in group 2, 22 is a service item, 23 is not in use
    private static readonly Dictionary<string, AlternateItemCandidate> Items = new[]
    {
        new AlternateItemCandidate(10, "A10", "INVENTORY", "Y", Group, "G1"),
        new AlternateItemCandidate(11, "A11", "INVENTORY", "Y", Group, "G1"),
        new AlternateItemCandidate(20, "B20", "INVENTORY", "Y", null, null),
        new AlternateItemCandidate(21, "B21", "INVENTORY", "Y", OtherGroup, "G2"),
        new AlternateItemCandidate(22, "S22", "SERVICE", "Y", null, null),
        new AlternateItemCandidate(23, "N23", "INVENTORY", "N", null, null),
    }.ToDictionary(i => i.ItemCd);

    private static readonly long[] Members = [10, 11];

    [Fact, Trait("Id", "MM-UT-811"), Trait("Id", "BC-UC-012-T01"), Trait("Rule", "MM-BR-811")]
    public void Plan_AddChangeAndDelete_JoinsTheNewItems_AndClearsTheRemovedOnes()
    {
        // Row of 10 deleted, row of 11 changed to B20
        var joinCodes = AlternateItemRules.JoinCodes([], [new(11, " b20 ")]);

        var plan = AlternateItemRules.Plan(Group, Members, [10, 11], joinCodes, Items);

        joinCodes.Should().Equal("B20");
        plan.JoinIds.Should().Equal(20);
        plan.LeaveIds.Should().Equal(10, 11);
        plan.Moved.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-811"), Trait("Rule", "MM-BR-811")]
    public void Plan_AnItemThatLeavesAndComesBackOnAnotherRow_StaysWithoutWrites()
    {
        var plan = AlternateItemRules.Plan(Group, Members, [10], AlternateItemRules.JoinCodes(["A10"], []), Items);

        plan.JoinIds.Should().BeEmpty();
        plan.LeaveIds.Should().BeEmpty();
    }

    [Theory, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    [InlineData("S22")]
    [InlineData("N23")]
    [InlineData("ZZZ")]
    [InlineData("A10")]
    public void Plan_AServiceItem_AnItemNotInUse_AnUnknownCode_OrAMember_IsMmE812(string code)
    {
        var act = () => AlternateItemRules.Plan(Group, Members, [], [code], Items);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(AlternateItemRules.InvalidItemCode);
        error.Args.Should().Equal(code);
    }

    [Fact, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    public void Plan_TheSameItemOnTwoRows_IsMmE812()
    {
        var act = () => AlternateItemRules.Plan(Group, Members, [], AlternateItemRules.JoinCodes(["B20", "b20"], []), Items);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("B20");
    }

    [Fact, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    public void Plan_AnItemOfAnotherGroup_JoinsAndIsReportedAsMoved()
    {
        var plan = AlternateItemRules.Plan(Group, Members, [], ["B21"], Items);

        plan.JoinIds.Should().Equal(21);
        plan.Moved.Select(m => (m.ItemCd, m.GroupCd)).Should().Equal(("B21", "G2"));
    }

    [Fact, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    public void JoinCodes_ARowWithoutItemCode_IsMmE802()
    {
        var act = () => AlternateItemRules.JoinCodes(["B20"], [new(10, " ")]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(Item.RequiredCode);
    }

    [Fact, Trait("Id", "MM-UT-813"), Trait("Id", "BC-UC-012-T02"), Trait("Rule", "MM-BR-813")]
    public void Plan_SeveralBadRows_ReportsTheFirstInRowOrder_AddedRowsFirst()
    {
        var joinCodes = AlternateItemRules.JoinCodes(["B20", "S22"], [new(11, "N23")]);

        var act = () => AlternateItemRules.Plan(Group, Members, [11], joinCodes, Items);

        joinCodes.Should().Equal("B20", "S22", "N23");
        act.Should().Throw<DomainException>().Which.Args.Should().Equal("S22");
    }
}

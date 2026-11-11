using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Items;

/// <summary>Alternate item groups (BC-UC-019, MM-BR-810, MM-BR-813).</summary>
public class AlternateItemGroupRulesTests
{
    [Fact, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    public void Normalize_TrimsAndUpperCasesTheCode_AndTrimsTheName()
    {
        AlternateItemGroupRules.Normalize(" bolt-m6 ", " Bolts ").Should().Be(new AlternateItemGroupFields("BOLT-M6", "Bolts"));
    }

    [Theory, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    [InlineData(null, "Bolts", "")]
    [InlineData(" ", "Bolts", "")]
    [InlineData("ABCDEFGHIJKLMNOPQRS", "Bolts", "ABCDEFGHIJKLMNOPQRS")]
    [InlineData("bolt", " ", "BOLT")]
    [InlineData("bolt", null, "BOLT")]
    public void Normalize_NoCode_ACodeOver18_OrNoName_IsMmE810(string? code, string? name, string arg)
    {
        var act = () => AlternateItemGroupRules.Normalize(code, name);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(AlternateItemGroupRules.InvalidCode);
        error.Args.Should().Equal(arg);
    }

    [Fact, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    public void Normalize_A18CharacterCode_IsAccepted()
    {
        AlternateItemGroupRules.Normalize(new string('A', 18), "Bolts").AltItemGroupCd.Should().HaveLength(18);
    }

    [Theory, Trait("Id", "MM-UT-810"), Trait("Id", "MM-UT-813"), Trait("Rule", "MM-BR-810"), Trait("Rule", "MM-BR-813")]
    [InlineData("NUT,BOLT,NUT", "", "NUT")]
    [InlineData("NUT,GEAR,BOLT", "GEAR,BOLT", "GEAR")]
    public void EnsureUnique_ACodeOnTwoRowsOrOfAStayingGroup_IsMmE810ForTheFirstClash(string rows, string taken, string clash)
    {
        var act = () => AlternateItemGroupRules.EnsureUnique(
            rows.Split(','), taken.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet());

        act.Should().Throw<DomainException>().Which.Args.Should().Equal(clash);
    }

    [Fact, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    public void EnsureUnique_DistinctCodesNotTaken_Pass()
    {
        var act = () => AlternateItemGroupRules.EnsureUnique(["NUT", "BOLT"], new HashSet<string> { "GEAR" });

        act.Should().NotThrow();
    }
}

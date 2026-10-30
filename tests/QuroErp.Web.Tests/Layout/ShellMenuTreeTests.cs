using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Components.Layout;

namespace QuroErp.Web.Tests.Layout;

/// <summary>Side menu tree from the /me menus (TASK-CORE-037, app-shell.md 3).</summary>
public class ShellMenuTreeTests
{
    private static AuthMenu Group(string code, string? parent = null) => new(code, code, null, "bc", parent, null, "M");

    private static AuthMenu Program(string code, string parent, string path, string type = "P") =>
        new(code, code, path, "bc", parent, "Y", type);

    [Fact, Trait("Id", "CORE-037-UT-01")]
    public void Build_GroupsAndLinks_InMenuCodeOrder()
    {
        var menus = new[]
        {
            Program("G5AD02", "G5AD", "/bc/currency"),
            Group("G5AD"),
            Program("G1BA02", "G1BA", "/bc/code"),
            Program("G1BA01", "G1BA", "/bc/item"),
            Group("G1BA"),
        };

        var groups = ShellMenuTree.Build(menus, "/");

        groups.Select(g => g.MenuCode).Should().Equal("G1BA", "G5AD");
        groups[0].Entries.Select(e => e.Link!.MenuCode).Should().Equal("G1BA01", "G1BA02");
        groups[0].Entries[0].Link!.Path.Should().Be("/bc/item");
    }

    [Fact, Trait("Id", "CORE-037-UT-02")]
    public void Build_SubheadingInGroup_KeepsItsLinksUnderIt()
    {
        var menus = new[]
        {
            Group("G2MM"),
            Program("G2MM01", "G2MM", "/mm/purchase-order"),
            Group("G2MM10", "G2MM"),
            Program("G2MM11", "G2MM10", "/mm/goods-receipt"),
        };

        var entries = ShellMenuTree.Build(menus, "/").Single().Entries;

        entries[0].Link!.MenuCode.Should().Be("G2MM01");
        entries[1].HeadingCode.Should().Be("G2MM10");
        entries[1].HeadingLinks.Select(l => l.MenuCode).Should().Equal("G2MM11");
    }

    [Fact, Trait("Id", "CORE-037-UT-03")]
    public void Build_PopupsEmptyGroupsAndTopLevelPrograms_AreNotShown()
    {
        var menus = new[]
        {
            Group("G1BA"),
            Program("G1BA01", "G1BA", "/bc/item"),
            Program("G1BA90", "G1BA", "/bc/item-popup", type: "U"),
            Group("G1BA10", "G1BA"),
            Group("G3SD"),
            new AuthMenu("TOP01", "Top", "/bc/top", "bc", null, "Y", "P"),
        };

        var groups = ShellMenuTree.Build(menus, "/");

        groups.Should().ContainSingle().Which.MenuCode.Should().Be("G1BA");
        groups[0].Entries.Should().ContainSingle().Which.Link!.MenuCode.Should().Be("G1BA01");
    }

    [Theory, Trait("Id", "CORE-037-UT-04")]
    [InlineData("/bc/code-major", "G1BA03")]
    [InlineData("/bc/code-major/12?tab=1", "G1BA03")]
    [InlineData("/BC/CODE", "G1BA02")]
    public void Build_CurrentPath_MarksTheLongestMatchingLinkAndItsGroup(string path, string current)
    {
        var menus = new[]
        {
            Group("G1BA"),
            Group("G5AD"),
            Program("G1BA02", "G1BA", "/bc/code"),
            Program("G1BA03", "G1BA", "/bc/code-major"),
            Program("G5AD01", "G5AD", "/bc/company"),
        };

        var groups = ShellMenuTree.Build(menus, path);

        groups.SelectMany(g => g.Entries).Where(e => e.Link!.Current).Select(e => e.Link!.MenuCode).Should().Equal(current);
        groups.Where(g => g.Current).Select(g => g.MenuCode).Should().Equal("G1BA");
    }

    [Fact, Trait("Id", "CORE-037-UT-05")]
    public void Build_NoMenus_ReturnsNoGroups() =>
        ShellMenuTree.Build([], "/").Should().BeEmpty();
}

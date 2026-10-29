using FluentAssertions;
using QuroErp.Domain.Bc.Menus;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Menus;

/// <summary>Company menu rules (BC-BR-201~205, 207).</summary>
public class MenuRulesTests
{
    [Theory, Trait("Id", "BC-UT-201")]
    [InlineData(" item ", "ITEM")]
    [InlineData("ABCDEFGHIJ", "ABCDEFGHIJ")]
    public void Code_IsTrimmedAndUpperCase(string input, string expected) => MenuRules.NormalizeCode(input).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-201")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("ABCDEFGHIJK")]
    public void MissingOrLongCode_IsBcE201(string? input)
    {
        var act = () => MenuRules.NormalizeCode(input);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidCodeCode);
    }

    [Theory, Trait("Id", "BC-UT-202")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("1234567890123456789012345678901")]
    public void MissingOrLongName_IsBcE202WithTheMenuCode(string? name)
    {
        var act = () => MenuRules.Normalize("ITEM", name, "P", "/bc/item", "bc", null, null);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(MenuRules.InvalidNameCode);
        error.Args.Should().Equal("ITEM");
    }

    [Theory, Trait("Id", "BC-UT-203")]
    [InlineData("X", "/bc/item")]
    [InlineData("P", null)]
    [InlineData("U", " ")]
    [InlineData("M", "/bc/item")]
    public void BadTypeOrProgram_IsBcE203(string type, string? programId)
    {
        var act = () => MenuRules.Normalize("ITEM", "Item", type, programId, "bc", null, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidTypeCode);
    }

    [Theory, Trait("Id", "BC-UT-204")]
    [InlineData(null)]
    [InlineData("hr")]
    public void UnknownModule_IsBcE204(string? module)
    {
        var act = () => MenuRules.Normalize("ITEM", "Item", "P", "/bc/item", module, null, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidModuleCode);
    }

    [Fact, Trait("Id", "BC-UT-204")]
    public void ValidRow_IsNormalized()
    {
        var fields = MenuRules.Normalize("ITEM", " Item ", "p", " /bc/item ", "BC", " base ", null);

        fields.Should().Be(new MenuFields("Item", "P", "/bc/item", "bc", "BASE", "Y"));
    }

    [Fact, Trait("Id", "BC-UT-204")]
    public void GroupMenu_HasNoProgram() =>
        MenuRules.Normalize("BASE", "Base", "M", null, "bc", null, "n").Should().Be(new MenuFields("Base", "M", null, "bc", null, "N"));

    [Fact, Trait("Id", "BC-UT-205")]
    public void ParentThatIsNotAGroup_IsBcE205()
    {
        MenuNode[] tree = [new("ITEM", "P", null), new("USER", "P", "ITEM")];

        var act = () => MenuRules.EnsureParents(tree, ["USER"]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(MenuRules.InvalidParentCode);
        error.Args.Should().Equal("USER");
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public void MissingParent_IsBcE205()
    {
        var act = () => MenuRules.EnsureParents([new MenuNode("ITEM", "P", "NONE")], ["ITEM"]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidParentCode);
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public void Cycle_IsBcE205()
    {
        MenuNode[] tree = [new("A", "M", "C"), new("B", "M", "A"), new("C", "M", "B")];

        var act = () => MenuRules.EnsureParents(tree, ["A"]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidParentCode);
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public void OwnParent_IsBcE205()
    {
        var act = () => MenuRules.EnsureParents([new MenuNode("A", "M", "A")], ["A"]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InvalidParentCode);
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public void GroupParents_AreAllowed() =>
        MenuRules.EnsureParents([new("BASE", "M", null), new("MASTER", "M", "BASE"), new("ITEM", "P", "MASTER")], ["ITEM", "MASTER"]);

    [Fact, Trait("Id", "BC-UT-207")]
    public void MenuAssignedToARole_IsBcE207()
    {
        var act = () => MenuRules.EnsureDeletable("ITEM", assignedToRole: true, []);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(MenuRules.InUseCode);
        error.Args.Should().Equal("ITEM");
    }

    [Fact, Trait("Id", "BC-UT-207")]
    public void MenuWithChildren_IsBcE207()
    {
        var act = () => MenuRules.EnsureDeletable("BASE", assignedToRole: false, [new MenuNode("ITEM", "P", "BASE")]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(MenuRules.InUseCode);
    }

    [Fact, Trait("Id", "BC-UT-207")]
    public void UnusedLeafMenu_CanBeDeleted() => MenuRules.EnsureDeletable("ITEM", assignedToRole: false, [new MenuNode("USER", "P", null)]);
}

using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Domain.Tests.Common.Codes;

/// <summary>Minor code rules (SD-BR-911~916).</summary>
public class CodeMinorRulesTests
{
    private static readonly Dictionary<string, string?> EnOnly = new() { ["en"] = "Each" };

    [Theory, Trait("Id", "SD-UT-911")]
    [InlineData(" ea ", "EA")]
    [InlineData("box_12", "BOX_12")]
    [InlineData("ABCDEFGHIJKLMNOPQRST", "ABCDEFGHIJKLMNOPQRST")]
    public void Code_IsTrimmedAndUpperCase(string input, string expected) => CodeMinorRules.NormalizeCode(input).Should().Be(expected);

    [Theory, Trait("Id", "SD-UT-911")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    [InlineData("EA-1")]
    [InlineData("박스")]
    public void MissingLongOrNonAsciiCode_IsSdE911(string? input)
    {
        var act = () => CodeMinorRules.NormalizeCode(input);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMinorRules.InvalidCodeCode);
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public void Names_AreTrimmed_BlankOnesRemoveTheTranslation_AndInUseIsTheDefault()
    {
        var fields = CodeMinorRules.Normalize("EA", new Dictionary<string, string?> { ["en"] = " Each ", ["KO-kr"] = "개", ["ja"] = " " }, 10, null, " a ", " ");

        fields.Names.Should().Equal(new Dictionary<string, string?> { ["en"] = "Each", ["ko"] = "개", ["ja"] = null });
        fields.UseYn.Should().BeTrue();
        fields.Attr1.Should().Be("a");
        fields.Attr2.Should().BeNull();
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public void WithoutAnEnName_IsSdE911WithTheCode()
    {
        var act = () => CodeMinorRules.Normalize("EA", new Dictionary<string, string?> { ["ko"] = "개", ["en"] = " " }, 10, true, null, null);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMinorRules.InvalidCodeCode);
        error.Args.Should().Equal("EA");
    }

    [Theory, Trait("Id", "SD-UT-912")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ea ")]
    public void SameOrNoCode_IsAllowed(string? requested) => CodeMinorRules.EnsureSameCode("EA", requested);

    [Fact, Trait("Id", "SD-UT-912")]
    public void ChangedCode_IsSdE912()
    {
        var act = () => CodeMinorRules.EnsureSameCode("EA", "BOX");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMinorRules.CodeChangedCode);
        error.Args.Should().Equal("EA");
    }

    [Theory, Trait("Id", "SD-UT-913")]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void SortOrderBelowOne_IsSdE913(int? sortOrder)
    {
        var act = () => CodeMinorRules.Normalize("EA", EnOnly, sortOrder, true, null, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMinorRules.InvalidSortOrderCode);
    }

    [Fact, Trait("Id", "SD-UT-913")]
    public void DuplicateSortOrder_IsSdE913WithTheLaterCode()
    {
        var act = () => CodeMinorRules.EnsureUnique([new("EA", 10), new("BOX", 20), new("SET", 10)]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMinorRules.InvalidSortOrderCode);
        error.Args.Should().Equal("SET");
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public void DuplicateCode_IsSdE911()
    {
        var act = () => CodeMinorRules.EnsureUnique([new("EA", 10), new("EA", 20)]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMinorRules.InvalidCodeCode);
        error.Args.Should().Equal("EA");
    }

    [Fact, Trait("Id", "SD-UT-914")]
    public void UsedCode_IsSdE914_UnusedCanBeDeleted()
    {
        var act = () => CodeMinorRules.EnsureDeletable("TERMS", "NET30", usedByData: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMinorRules.InUseCode);
        CodeMinorRules.EnsureDeletable("TERMS", "NET30", usedByData: false);
    }

    [Theory, Trait("Id", "SD-UT-916")]
    [InlineData(CodeMajors.ItemType, "INVENTORY")]
    [InlineData(CodeMajors.ValidFlag, "N")]
    [InlineData(CodeMajors.MovementType, "TR")]
    [InlineData(CodeMajors.Unit, "EA")]
    public void ConstantCode_IsSdE916(string major, string minor)
    {
        var deleteUnused = () => CodeMinorRules.EnsureDeletable(major, minor, usedByData: false);
        var setNotInUse = () => CodeMinorRules.EnsureNotProtected(major, minor);

        deleteUnused.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMinorRules.ProtectedCode);
        setNotInUse.Should().Throw<DomainException>().Which.Args.Should().Equal(minor);
    }

    [Fact, Trait("Id", "SD-UT-916")]
    public void OtherCodes_AreNotProtected()
    {
        CodeMinorRules.IsProtected(CodeMajors.Unit, "BOX").Should().BeFalse();
        CodeMinorRules.IsProtected("TERMS", "INVENTORY").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UT-914")]
    public void Usages_NameEveryConstantMajorThatDataStores()
    {
        CodeUsages.Of(CodeMajors.Unit).Should().Contain(new CodeColumn("bc_item", "unit"));
        CodeUsages.Of("CART_NO").Should().BeEmpty();
        CodeUsages.Columns.Values.SelectMany(c => c).Should().OnlyContain(c => c.Table.Length > 0 && c.Column.Length > 0);
    }
}

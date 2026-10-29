using System.Reflection;
using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Domain.Tests.Common.Codes;

/// <summary>Major code rules (SD-BR-901~904).</summary>
public class CodeMajorRulesTests
{
    [Theory, Trait("Id", "SD-UT-901")]
    [InlineData(" terms ", "TERMS")]
    [InlineData("so_status_2", "SO_STATUS_2")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ_123", "ABCDEFGHIJKLMNOPQRSTUVWXYZ_123")]
    public void Code_IsTrimmedAndUpperCase(string input, string expected) => CodeMajorRules.NormalizeCode(input).Should().Be(expected);

    [Theory, Trait("Id", "SD-UT-901")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ_1234")]
    [InlineData("SO-STATUS")]
    [InlineData("단위")]
    public void MissingLongOrNonAsciiCode_IsSdE901(string? input)
    {
        var act = () => CodeMajorRules.NormalizeCode(input);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public void Names_AreTrimmed_BlankOnesDropped_AndInUseIsTheDefault()
    {
        var fields = CodeMajorRules.Normalize("TERMS", new Dictionary<string, string?> { ["en"] = " Terms ", ["ko"] = " ", ["KO-kr"] = "결제 조건" }, null);

        fields.Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Terms", ["ko"] = "결제 조건" });
        fields.UseYn.Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public void WithoutAnEnName_IsSdE901WithTheCode()
    {
        var act = () => CodeMajorRules.Normalize("TERMS", new Dictionary<string, string?> { ["ko"] = "결제 조건" }, true);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
        error.Args.Should().Equal("TERMS");
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public void DuplicateCode_IsSdE901()
    {
        var act = () => CodeMajorRules.EnsureUnique(["TERMS", "UNIT", "TERMS"]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
        error.Args.Should().Equal("TERMS");
    }

    [Theory, Trait("Id", "SD-UT-902")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" terms ")]
    public void SameOrNoCode_IsAllowed(string? requested) => CodeMajorRules.EnsureSameCode("TERMS", requested);

    [Fact, Trait("Id", "SD-UT-902")]
    public void ChangedCode_IsSdE902()
    {
        var act = () => CodeMajorRules.EnsureSameCode("TERMS", "PAY_TERMS");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.CodeChangedCode);
        error.Args.Should().Equal("TERMS");
    }

    [Fact, Trait("Id", "SD-UT-903")]
    public void MajorWithMinors_IsSdE903_EmptyCanBeDeleted()
    {
        var act = () => CodeMajorRules.EnsureDeletable("TERMS", minorCount: 1);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.HasMinorsCode);
        error.Args.Should().Equal("TERMS");
        CodeMajorRules.EnsureDeletable("TERMS", minorCount: 0);
    }

    [Fact, Trait("Id", "SD-UT-904")]
    public void ConstantMajor_IsSdE904_EvenWithoutMinors()
    {
        var delete = () => CodeMajorRules.EnsureDeletable(CodeMajors.Unit, minorCount: 0);
        var setNotInUse = () => CodeMajorRules.EnsureNotProtected(CodeMajors.ValidFlag);

        delete.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMajorRules.ProtectedCode);
        setNotInUse.Should().Throw<DomainException>().Which.Args.Should().Equal(CodeMajors.ValidFlag);
        CodeMajorRules.EnsureNotProtected("TERMS");
    }

    [Fact, Trait("Id", "SD-UT-904")]
    public void EveryCodeMajorsConstant_IsProtected()
    {
        var constants = typeof(CodeMajors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

        CodeMajorRules.ProtectedMajors.Should().BeEquivalentTo(constants);
    }
}

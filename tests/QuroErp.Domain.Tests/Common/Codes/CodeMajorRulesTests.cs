using System.Reflection;
using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Domain.Tests.Common.Codes;

/// <summary>Major code rules (SD-BR-901~904).</summary>
public class CodeMajorRulesTests
{
    [Theory, Trait("Id", "SD-UT-901")]
    [InlineData(" territory ", "TERRITORY")]
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
    public void Names_AreTrimmed_BlankOnesRemoveTheTranslation_AndInUseIsTheDefault()
    {
        var fields = CodeMajorRules.Normalize("TERRITORY", new Dictionary<string, string?> { ["en"] = " Territory ", ["KO-kr"] = "영업 지역", ["ja"] = " " }, null);

        fields.Names.Should().Equal(new Dictionary<string, string?> { ["en"] = "Territory", ["ko"] = "영업 지역", ["ja"] = null });
        fields.UseYn.Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public void WithoutAnEnName_IsSdE901WithTheCode()
    {
        var act = () => CodeMajorRules.Normalize("TERRITORY", new Dictionary<string, string?> { ["ko"] = "영업 지역" }, true);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
        error.Args.Should().Equal("TERRITORY");
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public void DuplicateCode_IsSdE901()
    {
        var act = () => CodeMajorRules.EnsureUnique(["TERRITORY", "UNIT", "TERRITORY"]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
        error.Args.Should().Equal("TERRITORY");
    }

    [Theory, Trait("Id", "SD-UT-902")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" territory ")]
    public void SameOrNoCode_IsAllowed(string? requested) => CodeMajorRules.EnsureSameCode("TERRITORY", requested);

    [Fact, Trait("Id", "SD-UT-902")]
    public void ChangedCode_IsSdE902()
    {
        var act = () => CodeMajorRules.EnsureSameCode("TERRITORY", "PAY_TERMS");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.CodeChangedCode);
        error.Args.Should().Equal("TERRITORY");
    }

    [Fact, Trait("Id", "SD-UT-903")]
    public void MajorWithMinors_IsSdE903_EmptyCanBeDeleted()
    {
        var act = () => CodeMajorRules.EnsureDeletable("TERRITORY", minorCount: 1);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CodeMajorRules.HasMinorsCode);
        error.Args.Should().Equal("TERRITORY");
        CodeMajorRules.EnsureDeletable("TERRITORY", minorCount: 0);
    }

    [Fact, Trait("Id", "SD-UT-904")]
    public void ConstantMajor_IsSdE904_EvenWithoutMinors()
    {
        var delete = () => CodeMajorRules.EnsureDeletable(CodeMajors.Unit, minorCount: 0);
        var setNotInUse = () => CodeMajorRules.EnsureNotProtected(CodeMajors.ValidFlag);

        delete.Should().Throw<DomainException>().Which.Code.Should().Be(CodeMajorRules.ProtectedCode);
        setNotInUse.Should().Throw<DomainException>().Which.Args.Should().Equal(CodeMajors.ValidFlag);
        CodeMajorRules.EnsureNotProtected("TERRITORY");
    }

    [Fact, Trait("Id", "SD-UT-904")]
    public void EveryCodeMajorsConstant_IsProtected()
    {
        var constants = typeof(CodeMajors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

        CodeMajorRules.ProtectedMajors.Should().BeEquivalentTo(constants);
    }
}

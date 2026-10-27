using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Tests.Common.I18n;

public class TranslationTests
{
    [Theory, Trait("Id", "CORE-004-T01")]
    [InlineData("ko", "ko")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN", "en")]
    [InlineData("sw_KE", "sw")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Normalize_AnyCode_ReturnsPrimaryLanguageOrNull(string? code, string? expected)
    {
        LanguageCode.Normalize(code).Should().Be(expected);
    }

    [Fact, Trait("Id", "CORE-004-T02")]
    public void Pick_RequestedLanguageExists_ReturnsRequested()
    {
        var names = new Dictionary<string, string?> { ["ko"] = "등급", ["en"] = "Grade" };

        Translation.Pick(names, "en", "ko").Should().Be("Grade");
    }

    [Fact, Trait("Id", "BC-UT-402")]
    public void Pick_RequestedLanguageMissing_FallsBackToCompanyDefault()
    {
        var names = new Dictionary<string, string?> { ["ko"] = "등급", ["en"] = "Grade" };

        Translation.Pick(names, "sw", "ko").Should().Be("등급");
    }

    [Fact, Trait("Id", "CORE-004-T04")]
    public void Pick_RequestedAndCompanyDefaultMissing_FallsBackToEn()
    {
        var names = new Dictionary<string, string?> { ["en"] = "Grade", ["ko"] = " " };

        Translation.Pick(names, "sw", "ko").Should().Be("Grade");
    }

    [Fact, Trait("Id", "CORE-004-T05")]
    public void Pick_NoName_ReturnsNull()
    {
        Translation.Pick(new Dictionary<string, string?> { ["ko"] = null }, "ko", null).Should().BeNull();
    }

    [Theory, Trait("Id", "BC-UT-402")]
    [InlineData("sw", "ko", new[] { "sw", "ko", "en" })]
    [InlineData("ko-KR", "ko", new[] { "ko", "en" })]
    [InlineData(null, null, new[] { "en" })]
    [InlineData("en", "ko", new[] { "en", "ko" })]
    public void FallbackOrder_RequestThenCompanyDefaultThenEn_WithoutDuplicates(string? lang, string? companyDefault, string[] expected)
    {
        Translation.FallbackOrder(lang, companyDefault).Should().Equal(expected);
    }

    [Theory, Trait("Id", "BC-UT-401")]
    [InlineData("en", true)]
    [InlineData("fil", true)]
    [InlineData("EN", false)]
    [InlineData("e", false)]
    [InlineData("en-US", false)]
    [InlineData(null, false)]
    public void IsValid_PrimaryLanguageSubtagOnly(string? code, bool expected)
    {
        LanguageCode.IsValid(code).Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-UT-401")]
    public void Ensure_InvalidCode_ThrowsBcE401()
    {
        var act = () => LanguageCode.Ensure("EN");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(LanguageCode.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-401")]
    public void Seeded_IsEnKoSw()
    {
        LanguageCode.Seeded.Should().Equal("en", "ko", "sw");
    }
}

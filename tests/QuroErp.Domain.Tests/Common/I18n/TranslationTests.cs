using FluentAssertions;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Tests.Common.I18n;

public class TranslationTests
{
    [Theory, Trait("Id", "CORE-004-T01")]
    [InlineData("ko", "ko")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN", "en")]
    [InlineData("ja", "ko")]
    [InlineData(null, "ko")]
    [InlineData("", "ko")]
    public void Normalize_AnyCode_ReturnsSupportedLanguage(string? code, string expected)
    {
        LanguageCode.Normalize(code).Should().Be(expected);
    }

    [Fact, Trait("Id", "CORE-004-T02")]
    public void Pick_RequestedLanguageExists_ReturnsRequested()
    {
        var names = new Dictionary<string, string?> { ["ko"] = "등급", ["en"] = "Grade" };

        Translation.Pick(names, "en").Should().Be("Grade");
    }

    [Fact, Trait("Id", "CORE-004-T03")]
    public void Pick_RequestedLanguageMissing_FallsBackToKo()
    {
        var names = new Dictionary<string, string?> { ["ko"] = "등급" };

        Translation.Pick(names, "en").Should().Be("등급");
    }

    [Fact, Trait("Id", "CORE-004-T04")]
    public void Pick_RequestedAndKoMissing_FallsBackToEn()
    {
        var names = new Dictionary<string, string?> { ["en"] = "Grade", ["ko"] = " " };

        Translation.Pick(names, "ko").Should().Be("Grade");
    }

    [Fact, Trait("Id", "CORE-004-T05")]
    public void Pick_NoName_ReturnsNull()
    {
        Translation.Pick(new Dictionary<string, string?> { ["ko"] = null }, "ko").Should().BeNull();
    }
}

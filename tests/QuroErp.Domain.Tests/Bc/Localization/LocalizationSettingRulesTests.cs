using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class LocalizationSettingRulesTests
{
    private static readonly Dictionary<string, bool> Languages = new() { ["en"] = true, ["ko"] = true, ["sw"] = false };

    private static readonly CountryDecimal[] CountryDecimals =
    [
        new("KR", DecimalModule.Mm, 2), new("KR", DecimalModule.Sd, 0), new("US", DecimalModule.Mm, 4), new("US", DecimalModule.Sd, 2),
    ];

    private static readonly DecimalSetting[] Settings =
    [
        new(DecimalModule.Mm, 2, RoundingMode.HalfUp), new(DecimalModule.Sd, 0, RoundingMode.HalfUp),
    ];

    [Fact, Trait("Id", "BC-UT-401")]
    public void EnsureLanguages_EnOff_RejectsBcE401()
    {
        var act = () => LocalizationSettingRules.EnsureLanguages(Languages, [("en", false)], "ko");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(LanguageCode.InvalidCode);
        error.Args.Should().Equal("en");
    }

    [Theory, Trait("Id", "BC-UT-401")]
    [InlineData("ja")]
    [InlineData("ko")]
    public void EnsureLanguages_UnknownOrRepeatedLanguage_RejectsBcE401(string second)
    {
        var act = () => LocalizationSettingRules.EnsureLanguages(Languages, [("ko", true), (second, true)], "en");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(LanguageCode.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-403")]
    public void EnsureLanguages_CompanyDefaultLanguageOff_RejectsBcE403()
    {
        var act = () => LocalizationSettingRules.EnsureLanguages(Languages, [("ko", false)], "ko");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(LocalizationSettingRules.LanguageNotInUseCode);
        error.Args.Should().Equal("ko");
    }

    [Fact, Trait("Id", "BC-UT-403")]
    public void EnsureLanguages_ReturnsOnlyLanguagesSwitchedOff()
    {
        var turnedOff = LocalizationSettingRules.EnsureLanguages(Languages, [("en", true), ("ko", false), ("sw", false)], "en");

        turnedOff.Should().Equal("ko");
    }

    [Fact, Trait("Id", "BC-UT-425")]
    public void FindReducedInUse_CompanyCountryUnitPriceReducedWithDocuments_ReturnsRow()
    {
        var inUse = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("KR", DecimalModule.Mm, 1)], Settings, [], "KR", _ => true);

        inUse.Should().Be("KR MM");
    }

    [Fact, Trait("Id", "BC-UT-425")]
    public void FindReducedInUse_QtyReducedWithModuleDocuments_ReturnsModule()
    {
        var inUse = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [], Settings, [new(DecimalModule.Mm, 1, RoundingMode.HalfUp)], "KR", m => m == DecimalModule.Mm);

        inUse.Should().Be("MM");
    }

    [Fact, Trait("Id", "BC-UT-425")]
    public void FindReducedInUse_IncreaseOtherCountryOrNoDocuments_Allowed()
    {
        var withDocuments = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals,
            [new("KR", DecimalModule.Sd, 2), new("US", DecimalModule.Mm, 2)],
            Settings,
            [new(DecimalModule.Mm, 4, RoundingMode.Down)],
            "KR",
            _ => true);
        var withoutDocuments = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("KR", DecimalModule.Mm, 0)], Settings, [new(DecimalModule.Mm, 0, RoundingMode.HalfUp)], "KR", _ => false);

        withDocuments.Should().BeNull();
        withoutDocuments.Should().BeNull();
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData("JP", "MM")]
    [InlineData("KR", "SD")]
    public void FindReducedInUse_UnknownOrRepeatedCountryRow_RejectsBcE422(string countryCode, string module)
    {
        var act = () => LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("KR", DecimalModule.Sd, 0), new(countryCode, module, 2)], Settings, [], "KR", _ => false);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(DecimalSetting.InvalidCode);
        error.Args.Should().Equal($"{countryCode} {module}");
    }

    [Fact, Trait("Id", "BC-UT-422")]
    public void FindReducedInUse_RepeatedModule_RejectsBcE422()
    {
        var act = () => LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [], Settings, [new(DecimalModule.Sd, 1, RoundingMode.HalfUp), new(DecimalModule.Sd, 2, RoundingMode.HalfUp)], "KR", _ => false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UC-027-T04")]
    public void KrCompany_UsdUnitPriceHasTwoDecimals_KrwUnitPriceHasNone()
    {
        var policy = new DecimalPolicy(
            "KR", Settings, CountryDecimals, new Dictionary<string, string?> { ["USD"] = "US", ["KRW"] = "KR" });

        policy.UnitPriceDecimalsOf(DecimalModule.Sd, "USD").Should().Be(2);
        policy.UnitPriceDecimalsOf(DecimalModule.Sd, "KRW").Should().Be(0);
    }
}

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

    // EUR has a country without a decimal row, IDR has no country (BC-BR-428)
    private static readonly Dictionary<string, string?> CurrencyCountries = new()
    {
        ["KRW"] = "KR",
        ["USD"] = "US",
        ["EUR"] = "DE",
        ["IDR"] = null,
    };

    private static IReadOnlyCollection<string?> NoCurrency(string module) => [null];

    private static IReadOnlyCollection<string?> NoDocuments(string module) => [];

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
            CountryDecimals, [new("KR", DecimalModule.Mm, 1)], Settings, [], "KR", CurrencyCountries, NoCurrency);

        inUse.Should().Be("KR MM");
    }

    [Fact, Trait("Id", "BC-UT-425")]
    public void FindReducedInUse_QtyReducedWithModuleDocuments_ReturnsModule()
    {
        var inUse = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [], Settings, [new(DecimalModule.Mm, 1, RoundingMode.HalfUp)], "KR", CurrencyCountries,
            m => m == DecimalModule.Mm ? ["USD"] : []);

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
            CurrencyCountries,
            NoCurrency);
        var withoutDocuments = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("KR", DecimalModule.Mm, 0)], Settings, [new(DecimalModule.Mm, 0, RoundingMode.HalfUp)], "KR", CurrencyCountries,
            NoDocuments);

        withDocuments.Should().BeNull();
        withoutDocuments.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-425"), Trait("Id", "BC-UC-027-T03"), Trait("Rule", "BC-BR-425"), Trait("Rule", "BC-BR-429")]
    public void FindReducedInUse_KrCompanyWithUsdPurchaseLines_BlocksUsMm_ButNotUsSdOrKrMm()
    {
        IReadOnlyCollection<string?> UsdPurchases(string module) => module == DecimalModule.Mm ? ["USD"] : [];

        var usMm = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("US", DecimalModule.Mm, 2)], Settings, [], "KR", CurrencyCountries, UsdPurchases);
        var others = LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("US", DecimalModule.Sd, 0), new("KR", DecimalModule.Mm, 0)], Settings, [], "KR", CurrencyCountries, UsdPurchases);

        usMm.Should().Be("US MM");
        others.Should().BeNull();
    }

    [Theory, Trait("Id", "BC-UT-429"), Trait("Rule", "BC-BR-429")]
    [InlineData(DecimalModule.Sd, "USD", "US")]
    [InlineData(DecimalModule.Sd, "KRW", "KR")]
    [InlineData(DecimalModule.Sd, "EUR", "KR")]
    [InlineData(DecimalModule.Sd, "IDR", "KR")]
    [InlineData(DecimalModule.Sd, "XXX", "KR")]
    [InlineData(DecimalModule.Mm, null, "KR")]
    public void PriceRowCountry_IsTheCurrencyCountryWithARow_ElseTheCompanyCountry(string module, string? currency, string country)
    {
        LocalizationSettingRules.PriceRowCountry(module, currency, "KR", CurrencyCountries, CountryDecimals).Should().Be(country);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData("JP", "MM")]
    [InlineData("KR", "SD")]
    public void FindReducedInUse_UnknownOrRepeatedCountryRow_RejectsBcE422(string countryCode, string module)
    {
        var act = () => LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [new("KR", DecimalModule.Sd, 0), new(countryCode, module, 2)], Settings, [], "KR", CurrencyCountries, NoDocuments);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(DecimalSetting.InvalidCode);
        error.Args.Should().Equal($"{countryCode} {module}");
    }

    [Fact, Trait("Id", "BC-UT-422")]
    public void FindReducedInUse_RepeatedModule_RejectsBcE422()
    {
        var act = () => LocalizationSettingRules.FindReducedInUse(
            CountryDecimals, [], Settings, [new(DecimalModule.Sd, 1, RoundingMode.HalfUp), new(DecimalModule.Sd, 2, RoundingMode.HalfUp)], "KR",
            CurrencyCountries, NoDocuments);

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

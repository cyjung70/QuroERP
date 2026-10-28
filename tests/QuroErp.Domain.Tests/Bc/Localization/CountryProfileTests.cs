using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class CountryProfileTests
{
    [Fact, Trait("Id", "BC-UT-313")]
    public void Us_Defaults_UsdEnSalesTaxMonthFirstDate()
    {
        var us = CountryProfiles.Get("US");

        us.BaseCurrencyCode.Should().Be("USD");
        us.DefaultLangCode.Should().Be("en");
        us.TaxMethod.Should().Be(TaxMethod.SalesTax);
        us.DateFormat.Should().Be("MM/dd/yyyy");
        us.DefaultDecimalSettings.Should().BeEquivalentTo(new[]
        {
            new DecimalSetting(DecimalModule.Mm, 2, RoundingMode.HalfUp),
            new DecimalSetting(DecimalModule.Sd, 0, RoundingMode.HalfUp),
        });
        us.DefaultUnitPriceDecimals.Should().BeEquivalentTo(new[]
        {
            new CountryDecimal("US", DecimalModule.Mm, 4),
            new CountryDecimal("US", DecimalModule.Sd, 2),
        });
    }

    [Fact, Trait("Id", "BC-UT-313")]
    public void Kr_Defaults_KrwKoVatIsoDate()
    {
        var kr = CountryProfiles.Get("KR");

        kr.BaseCurrencyCode.Should().Be("KRW");
        kr.DefaultLangCode.Should().Be("ko");
        kr.TaxMethod.Should().Be(TaxMethod.Vat);
        kr.DateFormat.Should().Be("yyyy-MM-dd");
        kr.DefaultDecimalSettings.Should().BeEquivalentTo(new[]
        {
            new DecimalSetting(DecimalModule.Mm, 2, RoundingMode.HalfUp),
            new DecimalSetting(DecimalModule.Sd, 0, RoundingMode.HalfUp),
        });
        kr.DefaultUnitPriceDecimals.Should().BeEquivalentTo(new[]
        {
            new CountryDecimal("KR", DecimalModule.Mm, 2),
            new CountryDecimal("KR", DecimalModule.Sd, 0),
        });
    }

    [Fact, Trait("Id", "BC-UT-422")]
    public void DefaultUnitPriceDecimals_AllSupportedCountries()
    {
        CountryProfiles.DefaultUnitPriceDecimals.Select(d => (d.CountryCode, d.Module, d.UnitPriceDecimals))
            .Should().Equal(("KR", "MM", 2), ("KR", "SD", 0), ("US", "MM", 4), ("US", "SD", 2));
    }

    [Theory, Trait("Id", "BC-UT-313")]
    [InlineData("JP")]
    [InlineData("us")]
    [InlineData(null)]
    public void Get_UnsupportedCountry_ThrowsBcE313(string? countryCode)
    {
        var act = () => CountryProfiles.Get(countryCode);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CountryProfiles.UnsupportedCode);
    }

    [Fact, Trait("Id", "BC-UT-313")]
    public void Supported_IsUsAndKr()
    {
        CountryProfiles.Supported.Should().BeEquivalentTo("US", "KR");
    }
}

using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class DecimalPolicyTests
{
    // bc_currency: USD = US, KRW = KR, EUR has no country, JPY's country has no bc_country_decimal row
    private static readonly Dictionary<string, string?> CurrencyCountries = new()
    {
        ["USD"] = "US",
        ["KRW"] = "KR",
        ["EUR"] = null,
        ["JPY"] = "JP",
    };

    private static DecimalPolicy For(string companyCountry) => new(
        companyCountry, CountryProfiles.Get(companyCountry).DefaultDecimalSettings, CountryProfiles.DefaultUnitPriceDecimals,
        CurrencyCountries);

    private static readonly DecimalPolicy Us = For("US");

    private static readonly DecimalPolicy Kr = For("KR");

    [Fact, Trait("Id", "BC-UT-429")]
    public void EnsureUnitPrice_KrCompanyUsdSdPrice_UsesUsRow()
    {
        var act = () => Kr.EnsureUnitPrice(DecimalModule.Sd, "USD", 12.50m, "unitPrice");

        act.Should().NotThrow();
        Kr.UnitPriceDecimalsOf(DecimalModule.Sd, "USD").Should().Be(2);
    }

    [Fact, Trait("Id", "BC-UT-429")]
    public void EnsureUnitPrice_KrCompanyKrwSdPriceWithFraction_RejectsBcE423()
    {
        var act = () => Kr.EnsureUnitPrice(DecimalModule.Sd, "KRW", 12.5m, "unitPrice");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
        error.Args.Should().Equal("unitPrice", 0);
    }

    [Fact, Trait("Id", "BC-UT-429")]
    public void EnsureUnitPrice_UsCompanyKrwSdPriceWithFraction_UsesKrRowAndRejects()
    {
        var act = () => Us.EnsureUnitPrice(DecimalModule.Sd, "KRW", 12.5m, "unitPrice");

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("unitPrice", 0);
    }

    [Theory, Trait("Id", "BC-UT-429")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    [InlineData("CHF")]
    [InlineData(null)]
    public void UnitPriceDecimalsOf_NoCurrencyCountryRow_UsesCompanyCountryRow(string? currencyCode)
    {
        Us.UnitPriceDecimalsOf(DecimalModule.Sd, currencyCode).Should().Be(2);
        Us.UnitPriceDecimalsOf(DecimalModule.Mm, currencyCode).Should().Be(4);
        Kr.UnitPriceDecimalsOf(DecimalModule.Sd, currencyCode).Should().Be(0);
        Kr.UnitPriceDecimalsOf(DecimalModule.Mm, currencyCode).Should().Be(2);
    }

    [Fact, Trait("Id", "BC-UT-429")]
    public void UnitPriceDecimalsOf_CompanyCountryWithoutRow_ThrowsBcE422()
    {
        var policy = new DecimalPolicy("US", [], [new CountryDecimal("KR", DecimalModule.Sd, 0)], CurrencyCountries);

        var act = () => policy.UnitPriceDecimalsOf(DecimalModule.Sd, "USD");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-423")]
    public void EnsureQty_WithinModuleDecimals_Passes()
    {
        var act = () =>
        {
            Us.EnsureQty(DecimalModule.Mm, 3.25m, "qty");
            Us.EnsureQty(DecimalModule.Sd, 3.00m, "qty");
        };

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "BC-UT-423")]
    public void EnsureQty_FractionWhenSdQtyHasNoDecimals_RejectsBcE423()
    {
        var act = () => Us.EnsureQty(DecimalModule.Sd, 1.5m, "qty");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }

    [Fact, Trait("Id", "BC-UT-424")]
    public void RoundUnitPrice_ComputedValue_UsesCurrencyCountryDecimalsAndModuleMode()
    {
        var policy = new DecimalPolicy(
            "US",
            [new DecimalSetting(DecimalModule.Mm, 2, RoundingMode.HalfUp), new DecimalSetting(DecimalModule.Sd, 0, RoundingMode.Down)],
            CountryProfiles.DefaultUnitPriceDecimals,
            CurrencyCountries);

        policy.RoundUnitPrice(DecimalModule.Mm, "USD", 1.23455m).Should().Be(1.2346m);
        policy.RoundUnitPrice(DecimalModule.Mm, "KRW", 1.235m).Should().Be(1.24m);
        policy.RoundUnitPrice(DecimalModule.Sd, "USD", 19.999m).Should().Be(19.99m);
        policy.RoundUnitPrice(DecimalModule.Sd, "KRW", 1999.9m).Should().Be(1999m);
        policy.RoundQty(DecimalModule.Mm, 2.005m).Should().Be(2.01m);
        policy.RoundQty(DecimalModule.Sd, 3.7m).Should().Be(3m);
    }

    [Fact, Trait("Id", "BC-UT-422")]
    public void SettingOf_ModuleWithoutSetting_ThrowsBcE422()
    {
        var act = () => new DecimalPolicy("US", [], [], CurrencyCountries).SettingOf(DecimalModule.Mm);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData("SD", 5)]
    [InlineData("SD", -1)]
    [InlineData("FI", 2)]
    public void DecimalSetting_OutOfRangeOrUnknownModule_ThrowsBcE422(string module, int qtyDecimals)
    {
        var act = () => new DecimalSetting(module, qtyDecimals, RoundingMode.HalfUp);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData("MM", 7)]
    [InlineData("MM", -1)]
    [InlineData("FI", 2)]
    public void CountryDecimal_OutOfRangeOrUnknownModule_ThrowsBcE422(string module, int unitPriceDecimals)
    {
        var act = () => new CountryDecimal("US", module, unitPriceDecimals);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData(6, 4)]
    [InlineData(0, 0)]
    public void Bounds_AreAllowed(int unitPriceDecimals, int qtyDecimals)
    {
        var act = () =>
        {
            _ = new CountryDecimal("KR", DecimalModule.Mm, unitPriceDecimals);
            _ = new DecimalSetting(DecimalModule.Mm, qtyDecimals, RoundingMode.Down);
        };

        act.Should().NotThrow();
    }
}

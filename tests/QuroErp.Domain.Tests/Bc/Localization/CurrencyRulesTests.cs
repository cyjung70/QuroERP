using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

/// <summary>Currency master rules (BC-BR-404, BC-BR-411, BC-BR-412, BC-BR-428, BC-BR-312).</summary>
public class CurrencyRulesTests
{
    private static readonly Dictionary<string, string?> EnName = new() { ["en"] = "Euro" };

    [Theory, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    [InlineData(" eur ", "EUR")]
    [InlineData("usd", "USD")]
    public void NormalizeCode_ThreeLetters_IsUpperCase(string input, string expected) =>
        CurrencyRules.NormalizeCode(input).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("U5D")]
    public void NormalizeCode_NotThreeLetters_IsBcE411(string? input)
    {
        var act = () => CurrencyRules.NormalizeCode(input);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CurrencyRules.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    [InlineData("USD", 2)]
    [InlineData("KRW", 0)]
    [InlineData("IDR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    public void Normalize_WithoutDecimals_UsesTheIso4217Value(string code, int decimals) =>
        CurrencyRules.Normalize(code, null, null, null, new Dictionary<string, string?> { ["en"] = code }).Decimals.Should().Be(decimals);

    [Theory, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    [InlineData(-1)]
    [InlineData(5)]
    public void Normalize_DecimalsOutside0To4_IsBcE411(int decimals)
    {
        var act = () => CurrencyRules.Normalize("EUR", decimals, null, null, EnName);

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = CurrencyRules.InvalidCode, Args = new object[] { "EUR" } });
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    public void EnsureUnique_ARepeatedCode_IsBcE411WithTheCode()
    {
        CurrencyRules.EnsureUnique(["USD", "KRW"]);

        var act = () => CurrencyRules.EnsureUnique(["USD", "KRW", "USD"]);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("USD");
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    public void EnsureSameCode_AnotherCode_IsBcE411_TheSameOrNone_Passes()
    {
        CurrencyRules.EnsureSameCode("USD", "usd");
        CurrencyRules.EnsureSameCode("USD", null);

        var act = () => CurrencyRules.EnsureSameCode("USD", "EUR");

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = CurrencyRules.InvalidCode, Args = new object[] { "USD" } });
    }

    [Theory, Trait("Id", "BC-UT-428"), Trait("Rule", "BC-BR-428")]
    [InlineData(" us ", "US")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_Country_IsUpperCaseAlpha2OrEmpty(string? input, string? expected) =>
        CurrencyRules.Normalize("USD", 2, input, null, EnName).CountryCode.Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-428"), Trait("Rule", "BC-BR-428")]
    [InlineData("USA")]
    [InlineData("U1")]
    [InlineData("K")]
    public void Normalize_InvalidCountry_IsBcE428(string country)
    {
        var act = () => CurrencyRules.Normalize("USD", 2, country, null, EnName);

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = CurrencyRules.InvalidCountryCode, Args = new object[] { "USD" } });
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411"), Trait("Rule", "BC-BR-428")]
    public void LockedByDocuments_DecimalsThenCountry_OtherChangesAllowed()
    {
        var stored = new StoredCurrency("USD", 2, "US");

        CurrencyRules.LockedByDocuments(stored, CurrencyRules.Normalize("USD", 2, "US", false, EnName)).Should().BeNull();
        CurrencyRules.LockedByDocuments(stored, CurrencyRules.Normalize("USD", 0, "KR", true, EnName)).Should().Be(CurrencyRules.InvalidCode);
        CurrencyRules.LockedByDocuments(stored, CurrencyRules.Normalize("USD", 2, null, true, EnName)).Should().Be(CurrencyRules.InvalidCountryCode);
    }

    [Fact, Trait("Id", "BC-UT-404"), Trait("Rule", "BC-BR-404")]
    public void Normalize_Names_EnRequired_BlankRemovesALanguage()
    {
        var fields = CurrencyRules.Normalize("EUR", 2, null, null, new Dictionary<string, string?> { ["EN"] = " Euro ", ["ko"] = " " });

        fields.Names.Should().Equal(new Dictionary<string, string?> { ["en"] = "Euro", ["ko"] = null });
        fields.UseYn.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-404"), Trait("Rule", "BC-BR-404")]
    public void Normalize_WithoutAnEnName_IsBcE404()
    {
        var act = () => CurrencyRules.Normalize("EUR", 2, null, null, new Dictionary<string, string?> { ["ko"] = "유로", ["en"] = " " });

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = CurrencyRules.NameRequiredCode, Args = new object[] { "EUR" } });
    }

    [Fact, Trait("Id", "BC-UT-312"), Trait("Rule", "BC-BR-312")]
    public void EnsureBaseStaysInUse_TheBaseSetNotInUse_IsBcE312()
    {
        CurrencyRules.EnsureBaseStaysInUse("USD", useYn: false, "KRW");
        CurrencyRules.EnsureBaseStaysInUse("KRW", useYn: true, "KRW");

        var act = () => CurrencyRules.EnsureBaseStaysInUse("KRW", useYn: false, "KRW");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(CurrencyRules.BaseCurrencyCode);
    }

    [Fact, Trait("Id", "BC-UT-412"), Trait("Rule", "BC-BR-412")]
    public void EnsureUsable_ACurrencyInUse_Passes_NotInUseOrUnknown_IsBcE412()
    {
        Currency[] currencies = [new("USD", 2), new("EUR", 2, inUse: false)];

        CurrencyRules.EnsureUsable("USD", currencies);

        foreach (var code in new[] { "EUR", "JPY" })
        {
            var act = () => CurrencyRules.EnsureUsable(code, currencies);
            act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = CurrencyRules.NotInUseCode, Args = new object[] { code } });
        }
    }
}

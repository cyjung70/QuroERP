using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Tax;

/// <summary>Tax code master rules (BC-UC-028, BC-BR-404, BC-BR-501~506).</summary>
public class TaxCodeRulesTests
{
    private static readonly Dictionary<string, string?> EnName = new() { ["en"] = "Tax" };
    private static readonly IReadOnlyList<string> Kr = CountryProfiles.Get(CountryProfiles.Kr).TaxKinds;
    private static readonly IReadOnlyList<string> Us = CountryProfiles.Get(CountryProfiles.Us).TaxKinds;
    private static readonly HashSet<long> Areas = [7];

    private static TaxCodeFields Kr1(string? kind, decimal? rate = null, long? area = null, bool? priceIncluded = null) =>
        TaxCodeRules.Normalize("T1", kind, rate, area, priceIncluded, null, EnName, Kr, Areas);

    private static TaxCodeFields Us1(string? kind, decimal? rate = null, long? area = null, bool? priceIncluded = null) =>
        TaxCodeRules.Normalize("T1", kind, rate, area, priceIncluded, null, EnName, Us, Areas);

    [Theory, Trait("Id", "BC-UT-501"), Trait("Rule", "BC-BR-501")]
    [InlineData(" vat10 ", "VAT10")]
    [InlineData("A", "A")]
    [InlineData("ABCDE12345", "ABCDE12345")]
    public void NormalizeCode_UpTo10LettersOrDigits_IsUpperCase(string input, string expected) =>
        TaxCodeRules.NormalizeCode(input).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-501"), Trait("Rule", "BC-BR-501")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDE123456")]
    [InlineData("VAT-10")]
    [InlineData("부가세")]
    public void NormalizeCode_EmptyLongOrOtherCharacters_IsBcE501(string? input)
    {
        var act = () => TaxCodeRules.NormalizeCode(input);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("BC-E-501");
    }

    [Fact, Trait("Id", "BC-UT-501"), Trait("Rule", "BC-BR-501")]
    public void EnsureUnique_ARepeatedCode_IsBcE501_WithTheCode()
    {
        var act = () => TaxCodeRules.EnsureUnique(["NONE", "VAT10", "VAT10"]);

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = "BC-E-501", Args = new object[] { "VAT10" } });
    }

    [Fact, Trait("Id", "BC-UT-404"), Trait("Rule", "BC-BR-404")]
    public void Normalize_WithoutAnEnName_IsBcE404()
    {
        var act = () => TaxCodeRules.Normalize("T1", TaxKind.VatZero, null, null, null, null, new Dictionary<string, string?> { ["ko"] = "영세" }, Kr, Areas);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("BC-E-404");
    }

    [Theory, Trait("Id", "BC-UC-028-T01"), Trait("Id", "BC-UT-502"), Trait("Rule", "BC-BR-502")]
    [InlineData("KR", "SALES_TAX")]
    [InlineData("KR", "SALES_TAX_EXEMPT")]
    [InlineData("US", "VAT_STANDARD")]
    [InlineData("US", "VAT_ZERO")]
    [InlineData("US", "OTHER")]
    [InlineData("US", null)]
    public void Normalize_AKindNotOfTheCountry_IsBcE502(string country, string? kind)
    {
        var act = () => country == "KR" ? Kr1(kind, area: 7) : Us1(kind);

        act.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = "BC-E-502", Args = new object[] { "T1" } });
    }

    [Fact, Trait("Id", "BC-UT-502"), Trait("Rule", "BC-BR-502")]
    public void Normalize_EveryKindOfTheCountry_IsKept()
    {
        Kr.Select(k => Kr1(k.ToLowerInvariant()).TaxKind).Should().Equal(Kr);
        Us.Select(k => Us1(k, area: 7).TaxKind).Should().Equal(Us);
    }

    [Theory, Trait("Id", "BC-UT-503"), Trait("Rule", "BC-BR-503")]
    [InlineData(null, "10")]
    [InlineData("0", "0")]
    [InlineData("12.5", "12.5")]
    [InlineData("99.9999", "99.9999")]
    public void Normalize_VatStandard_KeepsTheRate_10WhenEmpty(string? rate, string expected) =>
        Kr1(TaxKind.VatStandard, rate is null ? null : decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)).Rate
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Theory, Trait("Id", "BC-UT-503"), Trait("Rule", "BC-BR-503")]
    [InlineData("-0.1")]
    [InlineData("100")]
    [InlineData("10.12345")]
    public void Normalize_AVatRateOutOfRangeOrOver4Decimals_IsBcE503(string rate)
    {
        var act = () => Kr1(TaxKind.VatStandard, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("BC-E-503");
    }

    [Theory, Trait("Id", "BC-UC-028-T02"), Trait("Id", "BC-UT-503"), Trait("Rule", "BC-BR-503")]
    [InlineData(null)]
    [InlineData(8L)]
    public void Normalize_SalesTaxWithoutAKnownTaxArea_IsBcE503(long? area)
    {
        var act = () => Us1(TaxKind.SalesTax, area: area);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("BC-E-503");
    }

    [Fact, Trait("Id", "BC-UT-503"), Trait("Rule", "BC-BR-503")]
    public void Normalize_SalesTaxKeepsItsArea_AndOtherKindsHaveRate0AndNoArea()
    {
        Us1(TaxKind.SalesTax, 8.25m, 7).Should().BeEquivalentTo(new { Rate = 0m, TaxAreaId = 7L });
        Us1(TaxKind.SalesTaxExempt, 5m, 7).Should().BeEquivalentTo(new { Rate = 0m, TaxAreaId = (long?)null });
        Kr1(TaxKind.VatZero, 10m, 7).Should().BeEquivalentTo(new { Rate = 0m, TaxAreaId = (long?)null, UseYn = true, PriceIncluded = false });
    }

    [Theory, Trait("Id", "BC-UT-504"), Trait("Rule", "BC-BR-504")]
    [InlineData("KR", "VAT_ZERO")]
    [InlineData("KR", "NONE")]
    [InlineData("US", "SALES_TAX")]
    public void Normalize_PriceIncludedOnAnotherKind_IsBcE504(string country, string kind)
    {
        var act = () => country == "KR" ? Kr1(kind, priceIncluded: true) : Us1(kind, area: 7, priceIncluded: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("BC-E-504");
    }

    [Fact, Trait("Id", "BC-UT-504"), Trait("Rule", "BC-BR-504")]
    public void Normalize_PriceIncludedOnVatStandard_IsKept() =>
        Kr1(TaxKind.VatStandard, priceIncluded: true).PriceIncluded.Should().BeTrue();

    [Theory, Trait("Id", "BC-UT-505"), Trait("Rule", "BC-BR-505")]
    [InlineData("NONE", "NONE", true, false)]
    [InlineData("NONE", "NONE2", true, true)]
    [InlineData("NONE", "NONE", false, true)]
    [InlineData("VAT0", "VAT00", false, false)]
    public void ChangesNone_RenamingOrDisablingNone_IsTrue(string stored, string sent, bool useYn, bool expected) =>
        TaxCodeRules.ChangesNone(stored, sent, new TaxCodeFields(TaxKind.None, 0, null, false, useYn, EnName)).Should().Be(expected);

    [Fact, Trait("Id", "BC-UT-505"), Trait("Rule", "BC-BR-505")]
    public void ChangesNone_AnotherKind_IsTrue() =>
        TaxCodeRules.ChangesNone("NONE", "NONE", new TaxCodeFields(TaxKind.VatZero, 0, null, false, true, EnName)).Should().BeTrue();

    [Theory, Trait("Id", "BC-UT-506"), Trait("Rule", "BC-BR-506")]
    [InlineData("VAT_STANDARD", "10", null, false)]
    [InlineData("VAT_STANDARD", "11", null, true)]
    [InlineData("VAT_ZERO", "0", null, true)]
    [InlineData("VAT_STANDARD", "10", 7L, true)]
    public void ChangesWhatDocumentsUse_KindRateOrArea_IsTrue(string kind, string rate, long? area, bool expected) =>
        TaxCodeRules.ChangesWhatDocumentsUse(
            new StoredTaxCode("VAT10", TaxKind.VatStandard, 10m, null),
            new TaxCodeFields(kind, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), area, true, false, EnName))
            .Should().Be(expected);
}

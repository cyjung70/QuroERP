using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Tax;

public class TaxRuleTests
{
    private static readonly TaxAreaRate[] AreaRates = [new(new DateOnly(2026, 1, 1), 7.25m), new(new DateOnly(2026, 7, 1), 7.5m)];

    private static readonly TaxExemptionCertificate[] Certificates =
    [
        new("CA-1", "CA", new DateOnly(2026, 12, 31)),
        new("NY-1", "NY", null),
    ];

    [Theory, Trait("Id", "BC-UT-513")]
    [InlineData("2026-06-30", "7.25")]
    [InlineData("2026-07-01", "7.5")]
    [InlineData("2027-03-01", "7.5")]
    public void TaxAreaRate_IsTheLatestRowStartingOnOrBeforeTheDocumentDate(string date, string rate)
    {
        TaxAreaRate.EffectiveOn(AreaRates, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), "CA-LA")
            .Should().Be(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact, Trait("Id", "BC-UT-513")]
    public void TaxAreaRate_NoRowYet_IsBcE513()
    {
        var act = () => TaxAreaRate.EffectiveOn(AreaRates, new DateOnly(2025, 12, 31), "CA-LA");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(TaxAreaRate.NoRateCode);
        error.Args.Should().Equal("CA-LA", "2025-12-31");
    }

    [Theory, Trait("Id", "BC-UT-525")]
    [InlineData("CA", "2026-12-31", true, "CA-1", null)]
    [InlineData("CA", "2027-01-01", false, null, TaxExemptionDefault.ExpiredWarning)]
    [InlineData("NY", "2099-01-01", true, "NY-1", null)]
    [InlineData("TX", "2026-10-30", false, null, null)]
    public void Exemption_ValidCertificateOfTheState_GivesTheExemptCode_ExpiredOnlyWarns(
        string state, string date, bool exempt, string? certificateNo, string? warning)
    {
        TaxExemptionDefault.For(Certificates, state, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(new TaxExemptionDefault(exempt, certificateNo, warning));
    }

    [Theory, Trait("Id", "BC-UT-521")]
    [InlineData("220-81-62517", "2208162517")]
    [InlineData("1248100998", "1248100998")]
    [InlineData(" 124-81-00998 ", "1248100998")]
    public void BusinessRegistrationNo_Valid_IsStoredAsTenDigits(string value, string stored)
    {
        BusinessRegistrationNo.Normalize(value).Should().Be(stored);
    }

    [Theory, Trait("Id", "BC-UT-521")]
    [InlineData("2208162518")]
    [InlineData("220816251")]
    [InlineData("22081625170")]
    [InlineData("12481009A8")]
    [InlineData("")]
    [InlineData(null)]
    public void BusinessRegistrationNo_Invalid_IsBcE521(string? value)
    {
        var act = () => BusinessRegistrationNo.Normalize(value);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(BusinessRegistrationNo.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-505")]
    public void CountryProfiles_InstallTheirDefaultTaxCodes_WithTheirOwnKinds()
    {
        var us = CountryProfiles.Get(CountryProfiles.Us);
        var kr = CountryProfiles.Get(CountryProfiles.Kr);

        us.DefaultTaxCodes.Should().Equal(new DefaultTaxCode("EXEMPT", TaxKind.SalesTaxExempt, 0m), new DefaultTaxCode("NONE", TaxKind.None, 0m));
        kr.DefaultTaxCodes.Should().Equal(
            new DefaultTaxCode("VAT10", TaxKind.VatStandard, 10m), new DefaultTaxCode("VAT0", TaxKind.VatZero, 0m),
            new DefaultTaxCode("VATEX", TaxKind.VatExempt, 0m), new DefaultTaxCode("NONE", TaxKind.None, 0m));
        foreach (var profile in new[] { us, kr })
        {
            profile.DefaultTaxCodes.Select(c => c.Kind).Should().BeSubsetOf(profile.TaxKinds);
            profile.TaxKinds.Should().Contain(TaxKind.None).And.BeSubsetOf(TaxKind.All);
        }
    }
}

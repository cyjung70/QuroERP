using FluentAssertions;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Bc.Taxes;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Companies;

/// <summary>Company rules (BC-BR-302, 303, 313, 315, 316, 318) and the KR business number (BC-BR-521).</summary>
public class CompanyRulesTests
{
    private static readonly CompanyFields Us = new(
        "Quro", null, null, null, null, null, null, null, null, null, null, "US", "USD", "en", "America/New_York", null);

    [Fact, Trait("Id", "BC-UT-302")]
    public void Text_IsTrimmed_CodesAreCased_AndBillingModeDefaultsToWithShipment()
    {
        var fields = CompanyRules.Normalize(Us with { Name = " Quro Inc ", Email = " a@b.co ", Contact = " ", CountryCode = "us", DefaultLangCode = "EN" });

        fields.Should().Be(Us with { Name = "Quro Inc", Email = "a@b.co", BillingMode = "WITH_SHIPMENT" });
    }

    [Theory, Trait("Id", "BC-UT-302")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void MissingOrLongName_IsBcE302(string? name)
    {
        var act = () => CompanyRules.Normalize(Us with { Name = name });

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CompanyRules.InvalidNameCode);
        error.Args.Should().Equal("Name");
    }

    [Theory, Trait("Id", "BC-UT-303")]
    [InlineData("Billto", 201)]
    [InlineData("Shipto", 201)]
    [InlineData("Contact", 41)]
    [InlineData("TelNo", 21)]
    [InlineData("FaxNo", 21)]
    [InlineData("RegisterNo", 31)]
    [InlineData("Represent", 31)]
    [InlineData("BusinessType", 51)]
    [InlineData("BusinessItem", 51)]
    public void LongText_IsBcE303WithTheField(string field, int length)
    {
        var value = new string('x', length);
        var fields = field switch
        {
            "Billto" => Us with { Billto = value },
            "Shipto" => Us with { Shipto = value },
            "Contact" => Us with { Contact = value },
            "TelNo" => Us with { TelNo = value },
            "FaxNo" => Us with { FaxNo = value },
            "RegisterNo" => Us with { RegisterNo = value },
            "Represent" => Us with { Represent = value },
            "BusinessType" => Us with { BusinessType = value },
            _ => Us with { BusinessItem = value },
        };

        var act = () => CompanyRules.Normalize(fields);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CompanyRules.InvalidFieldCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "BC-UT-303")]
    public void TextAtTheLimits_IsAllowed() =>
        CompanyRules.Normalize(Us with { Billto = new string('x', 200), TelNo = new string('1', 20), Email = "abcdefghij@abcdefghijklmn.com", BusinessType = new string('x', 50) });

    [Theory, Trait("Id", "BC-UT-303")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("abcdefghijkl@abcdefghijklmn.com")]
    public void BadOrLongEmail_IsBcE303(string email)
    {
        var act = () => CompanyRules.Normalize(Us with { Email = email });

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("Email");
    }

    [Fact, Trait("Id", "BC-UT-313")]
    public void MvpCountries_AreUsAndKr() => CompanyRules.Countries.Should().BeEquivalentTo(["US", "KR"]);

    [Theory, Trait("Id", "BC-UT-315")]
    [InlineData(null)]
    [InlineData("220-81-62518")]
    [InlineData("22081625")]
    [InlineData("22081625AB")]
    public void KrCompany_WithoutAValidRegisterNo_IsBcE315(string? registerNo)
    {
        var act = () => CompanyRules.Normalize(Us with { CountryCode = "KR", RegisterNo = registerNo });

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CompanyRules.RegisterNoCode);
        error.Args.Should().Equal("RegisterNo");
    }

    [Fact, Trait("Id", "BC-UT-315")]
    public void KrRegisterNo_IsStoredWithoutHyphens() =>
        CompanyRules.Normalize(Us with { CountryCode = "KR", RegisterNo = "220-81-62517" }).RegisterNo.Should().Be("2208162517");

    [Fact, Trait("Id", "BC-UT-315")]
    public void UsRegisterNo_IsFreeText() =>
        CompanyRules.Normalize(Us with { RegisterNo = "12-3456789" }).RegisterNo.Should().Be("12-3456789");

    [Theory, Trait("Id", "BC-UT-521")]
    [InlineData("2208162517", "2208162517")]
    [InlineData("123-45-67891", "1234567891")]
    [InlineData("1048145376", null)]
    [InlineData("", null)]
    public void KrBusinessNumber_ChecksTheCheckDigit(string input, string? expected) => KrBusinessNumber.Normalize(input).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-316")]
    [InlineData("separate", "SEPARATE")]
    [InlineData(" WITH_SHIPMENT ", "WITH_SHIPMENT")]
    public void BillingMode_IsUpperCase(string input, string expected) =>
        CompanyRules.Normalize(Us with { BillingMode = input }).BillingMode.Should().Be(expected);

    [Fact, Trait("Id", "BC-UT-316")]
    public void UnknownBillingMode_IsBcE316()
    {
        var act = () => CompanyRules.Normalize(Us with { BillingMode = "MONTHLY" });

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(CompanyRules.BillingModeCode);
        error.Args.Should().Equal("MONTHLY");
    }

    [Theory, Trait("Id", "BC-UT-316")]
    [InlineData("SEPARATE", "WITH_SHIPMENT", true)]
    [InlineData("WITH_SHIPMENT", "SEPARATE", false)]
    [InlineData("SEPARATE", "SEPARATE", false)]
    public void OnlySeparateToWithShipment_NeedsAllShipmentsBilled(string current, string next, bool expected) =>
        CompanyRules.NeedsAllShipmentsBilled(current, next).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-318"), Trait("Rule", "BC-BR-318")]
    [InlineData("America/New_York", true)]
    [InlineData("Asia/Seoul", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("Korea Standard Time", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void TimeZone_MustBeAKnownIanaName(string? timeZone, bool known) =>
        CompanyRules.IsKnownTimeZone(timeZone).Should().Be(known);

    [Fact, Trait("Id", "BC-UT-318"), Trait("Rule", "BC-BR-318")]
    public void Normalize_TrimsTheTimeZone() =>
        CompanyRules.Normalize(Us with { TimeZone = " Asia/Seoul " }).TimeZone.Should().Be("Asia/Seoul");
}

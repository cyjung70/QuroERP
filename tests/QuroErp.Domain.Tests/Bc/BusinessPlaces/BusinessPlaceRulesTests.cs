using FluentAssertions;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.BusinessPlaces;

/// <summary>Business place rules (BC-BR-319, BC-BR-320) with the KR registration number check (BC-BR-521).</summary>
public class BusinessPlaceRulesTests
{
    private static readonly BusinessPlaceFields Place = new(
        "Quro", null, null, null, null, null, null, null, null, null, null, null, true);

    [Fact, Trait("Id", "BC-UT-319"), Trait("Rule", "BC-BR-319")]
    public void EnsureCanAdd_WithTheOnePlace_IsBcE319()
    {
        var act = () => BusinessPlaceRules.EnsureCanAdd(1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(BusinessPlaceRules.SinglePlaceCode);
    }

    [Fact, Trait("Id", "BC-UT-319"), Trait("Rule", "BC-BR-319")]
    public void EnsureCanAdd_WithoutAPlace_IsAllowed() => BusinessPlaceRules.EnsureCanAdd(0);

    [Fact, Trait("Id", "BC-UT-319"), Trait("Rule", "BC-BR-319")]
    public void TheDefaultPlace_CannotBeTurnedOff()
    {
        var act = () => BusinessPlaceRules.Normalize(Place with { UseYn = false }, "US", isDefault: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(BusinessPlaceRules.SinglePlaceCode);
    }

    [Fact, Trait("Id", "BC-UT-320")]
    public void Text_IsTrimmed_AndBlankIsNull() =>
        BusinessPlaceRules.Normalize(Place with { BusinessPlaceName = " Quro Seoul ", CeoName = " ", City = " Seoul " }, "US", true)
            .Should().Be(Place with { BusinessPlaceName = "Quro Seoul", City = "Seoul" });

    [Theory, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void MissingOrLongName_IsBcE320(string? name)
    {
        var act = () => BusinessPlaceRules.Normalize(Place with { BusinessPlaceName = name }, "US", true);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(BusinessPlaceRules.InvalidFieldCode);
        error.Args.Should().Equal("BusinessPlaceName");
    }

    [Theory, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    [InlineData(null)]
    [InlineData("220-81-62518")]
    [InlineData("22081625")]
    [InlineData("22081625AB")]
    public void KrPlace_WithoutAValidRegistrationNumber_IsBcE320(string? regNo)
    {
        var act = () => BusinessPlaceRules.Normalize(Place with { BusinessRegNo = regNo }, "KR", true);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(BusinessPlaceRules.InvalidFieldCode);
        error.Args.Should().Equal("BusinessRegNo");
    }

    [Fact, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-521")]
    public void KrRegistrationNumber_IsStoredWithoutHyphens() =>
        BusinessPlaceRules.Normalize(Place with { BusinessRegNo = "220-81-62517" }, "KR", true).BusinessRegNo.Should().Be("2208162517");

    [Fact, Trait("Id", "BC-UT-320")]
    public void UsRegistrationNumber_IsOptionalFreeText()
    {
        BusinessPlaceRules.Normalize(Place, "US", true).BusinessRegNo.Should().BeNull();
        BusinessPlaceRules.Normalize(Place with { BusinessRegNo = "12-3456789" }, "US", true).BusinessRegNo.Should().Be("12-3456789");
    }

    [Theory, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    [InlineData("001")]
    [InlineData("00001")]
    [InlineData("00A1")]
    public void SubBusinessNumber_NotFourDigits_IsBcE320(string subNo)
    {
        var act = () => BusinessPlaceRules.Normalize(Place with { SubBusinessNo = subNo }, "US", true);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("SubBusinessNo");
    }

    [Fact, Trait("Id", "BC-UT-320")]
    public void SubBusinessNumber_OfFourDigits_IsAllowed() =>
        BusinessPlaceRules.Normalize(Place with { SubBusinessNo = "0001" }, "US", true).SubBusinessNo.Should().Be("0001");

    [Fact, Trait("Id", "BC-UT-320")]
    public void LongText_IsBcE320WithTheField()
    {
        var act = () => BusinessPlaceRules.Normalize(Place with { BusinessItem = new string('x', 51) }, "US", true);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("BusinessItem");
    }
}

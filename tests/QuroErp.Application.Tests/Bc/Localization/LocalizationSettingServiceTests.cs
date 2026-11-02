using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Localization;

public class LocalizationSettingServiceTests
{
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    private readonly FakeLocalizationRepository _localization = new() { Company = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul") };

    private readonly FakeLocalizationSettingRepository _settings = new();

    private LocalizationSettingService Service => new(_localization, _settings, _unitOfWork);

    private UpdateLocalizationSettingRequest Request(
        IReadOnlyList<LanguageSettingDto>? languages = null,
        IReadOnlyList<CountryDecimalDto>? countryDecimals = null,
        IReadOnlyList<DecimalSettingDto>? decimalSettings = null,
        string taxCalcMode = "DOCUMENT") =>
        new(languages ?? [], countryDecimals ?? [], decimalSettings ?? [], taxCalcMode, _settings.Version);

    [Fact, Trait("Id", "BC-UC-027-T06")]
    public async Task GetAsync_ReturnsEveryLanguageWithUseAndVersion()
    {
        _localization.Languages[2] = new LanguageRow("sw", 3, false);

        var dto = await Service.GetAsync(CancellationToken.None);

        dto.CountryCode.Should().Be("KR");
        dto.DefaultLangCode.Should().Be("ko");
        dto.TaxCalcMode.Should().Be("DOCUMENT");
        dto.Languages.Should().Equal(new LanguageSettingDto("en", true), new LanguageSettingDto("ko", true), new LanguageSettingDto("sw", false));
        dto.CountryDecimals.Should().HaveCount(4);
        dto.DecimalSettings.Should().Equal(new DecimalSettingDto("MM", 2, "HALF_UP"), new DecimalSettingDto("SD", 0, "HALF_UP"));
        dto.UpdatedAt.Should().Be(_settings.Version);
        _settings.VersionLocks.Should().Equal(false);
    }

    [Fact, Trait("Id", "BC-UC-027-T06")]
    public async Task UpdateAsync_ValidChange_SavesEveryPartInOneTransaction()
    {
        var dto = await Service.UpdateAsync(
            Request(
                [new("sw", false)],
                [new("US", "SD", 3)],
                [new("SD", 1, "DOWN")],
                "LINE"),
            "admin",
            CancellationToken.None);

        _settings.VersionLocks.Should().Equal(true, false);
        _settings.Languages.Should().Equal(new LanguageUseRow("sw", false));
        _settings.UserMoves.Should().ContainSingle().Which.From.Should().Equal("sw");
        _settings.CountryDecimals.Should().Equal(new CountryDecimalRow("US", "SD", 3));
        _settings.DecimalSettings.Should().Equal(new DecimalSettingRow("SD", 1, "DOWN"));
        _settings.TaxCalcMode.Should().Be("LINE");
        _settings.UpdatedBy.Should().Be("admin");
        dto.UpdatedAt.Should().Be(_settings.Version);
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-027-T01")]
    public async Task UpdateAsync_EnOff_RejectsBcE401WithoutWrites()
    {
        var act = () => Service.UpdateAsync(Request([new("en", false)]), "admin", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("BC-E-401");
        _settings.Languages.Should().BeEmpty();
        _unitOfWork.Commits.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-027-T02")]
    [InlineData("US", "MM", 7, 2, "HALF_UP", "BC-E-422")]
    [InlineData("US", "MM", 4, 5, "HALF_UP", "BC-E-422")]
    [InlineData("US", "MM", 4, 2, "HALF_EVEN", "BC-E-424")]
    [InlineData("JP", "MM", 2, 2, "HALF_UP", "BC-E-422")]
    public async Task UpdateAsync_DecimalsOutOfRuleOrUnknownRow_Rejects(
        string countryCode, string module, int unitPriceDecimals, int qtyDecimals, string roundingMode, string code)
    {
        var act = () => Service.UpdateAsync(
            Request(countryDecimals: [new(countryCode, module, unitPriceDecimals)], decimalSettings: [new(module, qtyDecimals, roundingMode)]),
            "admin",
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(code);
        _unitOfWork.Commits.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-027-T03")]
    [InlineData("KR", 1, 2, "KR MM")]
    [InlineData("US", 4, 1, "MM")]
    public async Task UpdateAsync_ReduceDecimalsDocumentsUse_Throws409BcE425(string countryCode, int unitPriceDecimals, int qtyDecimals, string row)
    {
        _settings.DocumentCurrencies[DecimalModule.Mm] = [null];

        var act = () => Service.UpdateAsync(
            Request(countryDecimals: [new(countryCode, "MM", unitPriceDecimals)], decimalSettings: [new("MM", qtyDecimals, "HALF_UP")]),
            "admin",
            CancellationToken.None);

        var error = (await act.Should().ThrowAsync<ConflictException>()).Which;
        error.Code.Should().Be(LocalizationSettingRules.DecimalsInUseCode);
        error.Args.Should().Equal(row);
        _settings.CountryDecimals.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-027-T03")]
    public async Task UpdateAsync_ReduceOtherCountryRowWithDocuments_Saves()
    {
        _settings.DocumentCurrencies[DecimalModule.Sd] = [null];

        await Service.UpdateAsync(Request(countryDecimals: [new("US", "SD", 1)]), "admin", CancellationToken.None);

        _settings.CountryDecimals.Should().Equal(new CountryDecimalRow("US", "SD", 1));
    }

    [Theory, Trait("Id", "BC-UC-027-T03"), Trait("Rule", "BC-BR-425"), Trait("Rule", "BC-BR-429")]
    [InlineData("USD", true)]
    [InlineData("KRW", false)]
    public async Task UpdateAsync_ReduceTheRowOfTheDocumentCurrencyCountry_Throws409BcE425(string currencyCode, bool blocked)
    {
        _settings.DocumentCurrencies[DecimalModule.Sd] = [currencyCode];

        var act = () => Service.UpdateAsync(Request(countryDecimals: [new("US", "SD", 1)]), "admin", CancellationToken.None);

        if (blocked)
        {
            (await act.Should().ThrowAsync<ConflictException>()).Which.Args.Should().Equal("US SD");
        }
        else
        {
            await act.Should().NotThrowAsync();
        }
    }

    [Fact, Trait("Id", "BC-UC-027-T05")]
    public async Task UpdateAsync_LanguageOff_MovesItsUsersToCompanyDefaultLanguage()
    {
        _localization.Company = new("US", "USD", "en", "DOCUMENT", "America/New_York");

        await Service.UpdateAsync(Request([new("ko", false), new("sw", true)]), "admin", CancellationToken.None);

        _settings.UserMoves.Should().ContainSingle();
        _settings.UserMoves[0].From.Should().Equal("ko");
        _settings.UserMoves[0].To.Should().Be("en");
    }

    [Fact, Trait("Id", "BC-UC-027-T05")]
    public async Task UpdateAsync_CompanyDefaultLanguageOff_RejectsBcE403()
    {
        var act = () => Service.UpdateAsync(Request([new("ko", false)]), "admin", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LocalizationSettingRules.LanguageNotInUseCode);
    }

    [Fact, Trait("Id", "BC-UC-027-T06")]
    public async Task UpdateAsync_StaleUpdatedAt_ThrowsConcurrency()
    {
        var request = Request() with { UpdatedAt = DateTimeOffset.UnixEpoch };

        var act = () => Service.UpdateAsync(request, "admin", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _settings.TaxCalcMode.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UC-027-T06")]
    public async Task UpdateAsync_NoCompany_ThrowsNotFoundBcE006()
    {
        _localization.Company = null;

        var act = () => Service.UpdateAsync(Request(), "admin", CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("BC-E-006");
    }
}

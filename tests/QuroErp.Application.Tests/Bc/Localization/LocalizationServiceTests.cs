using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Localization;

namespace QuroErp.Application.Tests.Bc.Localization;

public class LocalizationServiceTests
{
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    private readonly FakeLocalizationRepository _localization = new();

    private LocalizationService Service =>
        new(_localization, new TranslationService(new CurrencyNames(), _localization), _unitOfWork);

    [Fact, Trait("Id", "CORE-014-T01")]
    public async Task GetAsync_NoCompany_UsesUsProfileDefaults()
    {
        var dto = await Service.GetAsync("en", CancellationToken.None);

        dto.CountryCode.Should().Be("US");
        dto.BaseCurrencyCode.Should().Be("USD");
        dto.DefaultLangCode.Should().Be("en");
        dto.TaxMethod.Should().Be("SALES_TAX");
        dto.DateFormat.Should().Be("MM/dd/yyyy");
        dto.TaxCalcMode.Should().Be("DOCUMENT");
    }

    [Fact, Trait("Id", "CORE-014-T02")]
    public async Task GetAsync_KrCompany_ReturnsCompanyValuesAndOnlyItemsInUse()
    {
        _localization.Company = new CompanyLocalizationRow("KR", "KRW", "ko", "LINE");
        _localization.Languages[2] = new LanguageRow("sw", 3, false);
        _localization.Currencies[1] = new CurrencyRow(2, "USD", 2, false);

        var dto = await Service.GetAsync("en", CancellationToken.None);

        dto.CountryCode.Should().Be("KR");
        dto.BaseCurrencyCode.Should().Be("KRW");
        dto.DefaultLangCode.Should().Be("ko");
        dto.TaxMethod.Should().Be("VAT");
        dto.DateFormat.Should().Be("yyyy-MM-dd");
        dto.TaxCalcMode.Should().Be("LINE");
        dto.Languages.Should().Equal(new LanguageDto("en", 1), new LanguageDto("ko", 2));
        dto.Currencies.Should().Equal(new CurrencyDto("KRW", "Korean Won", 0));
        dto.DecimalSettings.Should().Equal(new DecimalSettingDto("MM", 4, 2, "HALF_UP"), new DecimalSettingDto("SD", 2, 0, "HALF_UP"));
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "CORE-014-T03")]
    public async Task GetAsync_CurrencyWithoutName_UsesCurrencyCode()
    {
        _localization.Currencies.Add(new CurrencyRow(3, "EUR", 2, true));

        var dto = await Service.GetAsync("ko", CancellationToken.None);

        dto.Currencies.Should().Contain(new CurrencyDto("EUR", "EUR", 2));
        dto.Currencies.Should().Contain(new CurrencyDto("USD", "미국 달러", 2));
    }

    private sealed class CurrencyNames : ITranslationRepository
    {
        private static readonly TranslationRow[] Rows =
        [
            new(1, "en", "Korean Won"), new(1, "ko", "대한민국 원"),
            new(2, "en", "US Dollar"), new(2, "ko", "미국 달러"),
        ];

        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationRow>>(
                target == TranslationTarget.CurrencyName ? Rows.Where(r => ownerIds.Contains(r.OwnerId)).ToList() : []);
    }
}

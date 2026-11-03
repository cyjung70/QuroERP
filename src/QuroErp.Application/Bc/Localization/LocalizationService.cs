using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Localization values for every screen (BC-UC-027, BC-BR-426).</summary>
public sealed class LocalizationService(
    ILocalizationRepository localization, TranslationService translations, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>
    /// Reads the company country values, languages and currencies in use, country unit price decimals and module decimals.
    /// Without a company the US profile defaults apply [추정].
    /// </summary>
    /// <param name="langCode">Language for currency names.</param>
    public async Task<LocalizationDto> GetAsync(string? langCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        var languages = await localization.FindLanguagesAsync(uow.Transaction, cancellationToken);
        var currencies = (await localization.FindCurrenciesAsync(uow.Transaction, cancellationToken)).Where(c => c.UseYn).ToList();
        var decimals = await localization.FindDecimalSettingsAsync(uow.Transaction, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(uow.Transaction, cancellationToken);
        var names = await translations.GetNamesAsync(
            TranslationTarget.CurrencyName, currencies.Select(c => c.Id).ToList(), langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var profile = CountryProfiles.Get(company?.CountryCode ?? CountryProfiles.Us);
        return new LocalizationDto(
            profile.CountryCode,
            company?.BaseCurrencyCode ?? profile.BaseCurrencyCode,
            company?.DefaultLangCode ?? profile.DefaultLangCode,
            company?.TimeZone ?? profile.DefaultTimeZone,
            profile.TaxMethod,
            profile.DateFormat,
            company?.TaxCalcMode ?? TaxCalcMode.Document,
            languages.Where(l => l.UseYn).Select(l => new LanguageDto(l.LangCode, l.SortOrder)).ToList(),
            currencies.Select(c => new CurrencyDto(c.CurrencyCode, names.GetValueOrDefault(c.Id, c.CurrencyCode), c.Decimals, c.CountryCode)).ToList(),
            countryDecimals.Select(d => new CountryDecimalDto(d.CountryCode, d.Module, d.UnitPriceDecimals)).ToList(),
            decimals.Select(d => new DecimalSettingDto(d.Module, d.QtyDecimals, d.RoundingMode)).ToList());
    }
}

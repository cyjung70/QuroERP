using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Bc.Localization;

/// <summary>
/// The localization setting screen (BC-UC-027): language use, country unit price decimals, module quantity decimals
/// and rounding mode, and the tax calculation unit.
/// </summary>
public sealed class LocalizationSettingService(
    ILocalizationRepository localization, ILocalizationSettingRepository settings, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Every language with its use, decimals and tax calculation unit. Without a company the US profile defaults apply.</summary>
    public async Task<LocalizationSettingDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(uow, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Saves the listed rows in one transaction. Users of a language switched off move to the company default language (BC-BR-403).
    /// </summary>
    /// <exception cref="Domain.Common.DomainException">
    /// BC-E-401 en off or unknown language, BC-E-403 company default language off, BC-E-422 decimals out of range or unknown row,
    /// BC-E-424 unknown rounding mode.
    /// </exception>
    /// <exception cref="ConflictException">BC-E-425 when decimals that documents use are reduced (BC-BR-425).</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored version.</exception>
    /// <exception cref="NotFoundException">BC-E-006 when there is no company yet.</exception>
    public async Task<LocalizationSettingDto> UpdateAsync(
        UpdateLocalizationSettingRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var requestedCountryDecimals = request.CountryDecimals
            .Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)).ToList();
        var requestedSettings = request.DecimalSettings
            .Select(d => new DecimalSetting(d.Module, d.QtyDecimals, Rounding.ParseMode(d.RoundingMode))).ToList();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        if (await settings.GetVersionAsync(forUpdate: true, uow.Transaction, cancellationToken) != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CompanyLogoService.CompanyNotFoundCode);

        var languages = await localization.FindLanguagesAsync(uow.Transaction, cancellationToken);
        var turnedOff = LocalizationSettingRules.EnsureLanguages(
            languages.ToDictionary(l => l.LangCode, l => l.UseYn),
            request.Languages.Select(l => (l.LangCode, l.UseYn)),
            company.DefaultLangCode);

        var currentCountryDecimals = (await localization.FindCountryDecimalsAsync(uow.Transaction, cancellationToken))
            .Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)).ToList();
        var currentSettings = (await localization.FindDecimalSettingsAsync(uow.Transaction, cancellationToken))
            .Select(d => new DecimalSetting(d.Module, d.QtyDecimals, Rounding.ParseMode(d.RoundingMode))).ToList();
        var currencyCountries = (await localization.FindCurrenciesAsync(uow.Transaction, cancellationToken))
            .ToDictionary(c => c.CurrencyCode, c => c.CountryCode, StringComparer.Ordinal);
        var documentCurrencies = new Dictionary<string, IReadOnlyCollection<string?>>();
        foreach (var module in DecimalModule.All)
        {
            documentCurrencies[module] = await settings.FindDocumentCurrenciesAsync(module, uow.Transaction, cancellationToken);
        }
        var inUse = LocalizationSettingRules.FindReducedInUse(
            currentCountryDecimals, requestedCountryDecimals, currentSettings, requestedSettings, company.CountryCode, currencyCountries,
            m => documentCurrencies[m]);
        if (inUse is not null)
        {
            throw new ConflictException(LocalizationSettingRules.DecimalsInUseCode, inUse);
        }

        await settings.UpdateLanguagesAsync(
            request.Languages.Select(l => new LanguageUseRow(l.LangCode, l.UseYn)).ToList(), updatedBy, uow.Transaction, cancellationToken);
        if (turnedOff.Count > 0)
        {
            await settings.MoveUserLanguagesAsync(turnedOff, company.DefaultLangCode, updatedBy, uow.Transaction, cancellationToken);
        }
        await settings.UpdateCountryDecimalsAsync(
            requestedCountryDecimals.Select(d => new CountryDecimalRow(d.CountryCode, d.Module, d.UnitPriceDecimals)).ToList(),
            updatedBy, uow.Transaction, cancellationToken);
        await settings.UpdateDecimalSettingsAsync(
            requestedSettings.Select(s => new DecimalSettingRow(s.Module, s.QtyDecimals, Rounding.ToCode(s.RoundingMode))).ToList(),
            updatedBy, uow.Transaction, cancellationToken);
        await settings.UpdateTaxCalcModeAsync(request.TaxCalcMode, updatedBy, uow.Transaction, cancellationToken);

        var dto = await ReadAsync(uow, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    private async Task<LocalizationSettingDto> ReadAsync(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        var languages = await localization.FindLanguagesAsync(uow.Transaction, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(uow.Transaction, cancellationToken);
        var decimals = await localization.FindDecimalSettingsAsync(uow.Transaction, cancellationToken);
        var version = await settings.GetVersionAsync(forUpdate: false, uow.Transaction, cancellationToken);

        var profile = CountryProfiles.Get(company?.CountryCode ?? CountryProfiles.Us);
        return new LocalizationSettingDto(
            profile.CountryCode,
            company?.BaseCurrencyCode ?? profile.BaseCurrencyCode,
            company?.DefaultLangCode ?? profile.DefaultLangCode,
            company?.TaxCalcMode ?? TaxCalcMode.Document,
            languages.Select(l => new LanguageSettingDto(l.LangCode, l.UseYn)).ToList(),
            countryDecimals.Select(d => new CountryDecimalDto(d.CountryCode, d.Module, d.UnitPriceDecimals)).ToList(),
            decimals.Select(d => new DecimalSettingDto(d.Module, d.QtyDecimals, d.RoundingMode)).ToList(),
            version);
    }
}

using System.Data;
using QuroErp.Application.Bc.Localization;

namespace QuroErp.Application.Tests.Bc.Localization;

internal sealed class FakeLocalizationRepository : ILocalizationRepository
{
    public CompanyLocalizationRow? Company { get; set; }

    public List<LanguageRow> Languages { get; } = [new("en", 1, true), new("ko", 2, true), new("sw", 3, true)];

    public List<CurrencyRow> Currencies { get; } = [new(1, "KRW", 0, true, "KR"), new(2, "USD", 2, true, "US")];

    public List<DecimalSettingRow> DecimalSettings { get; } = [new("MM", 2, "HALF_UP"), new("SD", 0, "HALF_UP")];

    public List<CountryDecimalRow> CountryDecimals { get; } =
        [new("KR", "MM", 2), new("KR", "SD", 0), new("US", "MM", 4), new("US", "SD", 2)];

    public Task<CompanyLocalizationRow?> FindCompanyAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Company);

    public Task<IReadOnlyList<LanguageRow>> FindLanguagesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LanguageRow>>(Languages);

    public Task<IReadOnlyList<CurrencyRow>> FindCurrenciesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CurrencyRow>>(Currencies);

    public Task<IReadOnlyList<DecimalSettingRow>> FindDecimalSettingsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DecimalSettingRow>>(DecimalSettings);

    public Task<IReadOnlyList<CountryDecimalRow>> FindCountryDecimalsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CountryDecimalRow>>(CountryDecimals);
}

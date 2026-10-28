using System.Data;
using QuroErp.Application.Bc.Localization;

namespace QuroErp.Application.Tests.Bc.Localization;

/// <summary>Records the writes; documents exist only for the modules in ModulesWithDocuments.</summary>
internal sealed class FakeLocalizationSettingRepository : ILocalizationSettingRepository
{
    public DateTimeOffset Version { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    public HashSet<string> ModulesWithDocuments { get; } = [];

    public List<bool> VersionLocks { get; } = [];

    public List<LanguageUseRow> Languages { get; } = [];

    public List<CountryDecimalRow> CountryDecimals { get; } = [];

    public List<DecimalSettingRow> DecimalSettings { get; } = [];

    public string? TaxCalcMode { get; private set; }

    public List<(IReadOnlyList<string> From, string To)> UserMoves { get; } = [];

    public string? UpdatedBy { get; private set; }

    public Task<DateTimeOffset> GetVersionAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        VersionLocks.Add(forUpdate);
        return Task.FromResult(Version);
    }

    public Task<bool> HasDocumentsAsync(string moduleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(ModulesWithDocuments.Contains(moduleCode));

    public Task UpdateLanguagesAsync(IReadOnlyList<LanguageUseRow> languages, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Languages.AddRange(languages);
        UpdatedBy = updatedBy;
        return Task.CompletedTask;
    }

    public Task UpdateCountryDecimalsAsync(
        IReadOnlyList<CountryDecimalRow> countryDecimals, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        CountryDecimals.AddRange(countryDecimals);
        return Task.CompletedTask;
    }

    public Task UpdateDecimalSettingsAsync(
        IReadOnlyList<DecimalSettingRow> settings, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        DecimalSettings.AddRange(settings);
        return Task.CompletedTask;
    }

    public Task UpdateTaxCalcModeAsync(string taxCalcMode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        TaxCalcMode = taxCalcMode;
        return Task.CompletedTask;
    }

    public Task<int> MoveUserLanguagesAsync(
        IReadOnlyList<string> fromLangCodes, string toLangCode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        UserMoves.Add((fromLangCodes, toLangCode));
        return Task.FromResult(1);
    }
}

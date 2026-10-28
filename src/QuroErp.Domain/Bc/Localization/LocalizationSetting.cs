using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>
/// Checks a localization setting change against the stored values (BC-UC-027): language use (BC-BR-401, BC-BR-403)
/// and reducing decimals that documents use (BC-BR-425). Range and rounding mode are checked by
/// <see cref="CountryDecimal"/>, <see cref="DecimalSetting"/> and <see cref="Rounding.ParseMode"/> (BC-BR-422, BC-BR-424).
/// </summary>
public static class LocalizationSettingRules
{
    /// <summary>The company default language is set to not in use. Arg: language code.</summary>
    public const string LanguageNotInUseCode = "BC-E-403";

    /// <summary>Decimals are reduced while documents use them. Arg: the row, e.g. "KR SD" (unit price) or "SD" (quantity).</summary>
    public const string DecimalsInUseCode = "BC-E-425";

    /// <summary>Languages the change switches off. Their users move to the company default language (BC-BR-403).</summary>
    /// <param name="current">Stored use_yn by language code.</param>
    /// <param name="requested">Requested use_yn. Languages not listed keep their value.</param>
    /// <exception cref="DomainException">
    /// BC-E-401 for an unknown or repeated language or en set to not in use; BC-E-403 when the company default language is switched off.
    /// </exception>
    public static IReadOnlyList<string> EnsureLanguages(
        IReadOnlyDictionary<string, bool> current, IEnumerable<(string LangCode, bool UseYn)> requested, string companyDefaultLangCode)
    {
        var seen = new HashSet<string>();
        var turnedOff = new List<string>();
        foreach (var (langCode, useYn) in requested)
        {
            if (langCode is null || !current.TryGetValue(langCode, out var stored) || !seen.Add(langCode) || (langCode == LanguageCode.En && !useYn))
            {
                throw new DomainException(LanguageCode.InvalidCode, langCode ?? "");
            }
            if (!useYn && langCode == companyDefaultLangCode)
            {
                throw new DomainException(LanguageNotInUseCode, langCode);
            }
            if (stored && !useYn)
            {
                turnedOff.Add(langCode);
            }
        }
        return turnedOff;
    }

    /// <summary>
    /// The first row whose decimals the change reduces while documents use it (BC-BR-425), or null. Increasing is always allowed.
    /// Unit price: document lines use the row of their currency country, else the company country row (BC-BR-429).
    /// Document lines have no currency yet (BC-BR-412), so only the company country row is in use [추정].
    /// Quantity: any document of the module.
    /// </summary>
    /// <param name="moduleHasDocuments">Whether documents of the module (MM, SD) exist.</param>
    /// <exception cref="DomainException">BC-E-422 for a row that does not exist or is repeated.</exception>
    public static string? FindReducedInUse(
        IReadOnlyList<CountryDecimal> currentCountryDecimals,
        IReadOnlyList<CountryDecimal> requestedCountryDecimals,
        IReadOnlyList<DecimalSetting> currentSettings,
        IReadOnlyList<DecimalSetting> requestedSettings,
        string companyCountryCode,
        Func<string, bool> moduleHasDocuments)
    {
        string? inUse = null;
        var seenRows = new HashSet<(string, string)>();
        foreach (var requested in requestedCountryDecimals)
        {
            var row = $"{requested.CountryCode} {requested.Module}";
            var current = currentCountryDecimals.FirstOrDefault(c => c.CountryCode == requested.CountryCode && c.Module == requested.Module);
            if (current is null || !seenRows.Add((requested.CountryCode, requested.Module)))
            {
                throw new DomainException(DecimalSetting.InvalidCode, row);
            }
            if (requested.UnitPriceDecimals < current.UnitPriceDecimals
                && requested.CountryCode == companyCountryCode && moduleHasDocuments(requested.Module))
            {
                inUse ??= row;
            }
        }

        var seenModules = new HashSet<string>();
        foreach (var requested in requestedSettings)
        {
            var current = currentSettings.FirstOrDefault(c => c.Module == requested.Module);
            if (current is null || !seenModules.Add(requested.Module))
            {
                throw new DomainException(DecimalSetting.InvalidCode, requested.Module);
            }
            if (requested.QtyDecimals < current.QtyDecimals && moduleHasDocuments(requested.Module))
            {
                inUse ??= requested.Module;
            }
        }
        return inUse;
    }
}

namespace QuroErp.Domain.Common.I18n;

/// <summary>Master data name translation (i18n.md "마스터 데이터 이름", BC-BR-402).</summary>
public static class Translation
{
    /// <summary>
    /// Picks the name to show: the first name found in requested language → company default language → en.
    /// </summary>
    /// <param name="namesByLang">Names by lang_code.</param>
    /// <param name="companyDefaultLangCode">bc_company.default_lang_code; null when there is no company yet.</param>
    /// <returns>Null when no language has a name.</returns>
    public static string? Pick(IReadOnlyDictionary<string, string?> namesByLang, string? langCode, string? companyDefaultLangCode)
    {
        foreach (var lang in FallbackOrder(langCode, companyDefaultLangCode))
        {
            if (namesByLang.TryGetValue(lang, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>Requested language → company default language → en, without duplicates or empty values.</summary>
    public static IReadOnlyList<string> FallbackOrder(string? langCode, string? companyDefaultLangCode) =>
        new[] { LanguageCode.Normalize(langCode), LanguageCode.Normalize(companyDefaultLangCode), LanguageCode.Fallback }
            .OfType<string>()
            .Distinct()
            .ToList();
}

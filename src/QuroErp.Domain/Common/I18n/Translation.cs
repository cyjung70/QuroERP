namespace QuroErp.Domain.Common.I18n;

/// <summary>마스터 데이터 이름 번역 (i18n.md "마스터 데이터 이름").</summary>
public static class Translation
{
    /// <summary>
    /// 보여줄 이름을 고른다: 요청 언어 → ko → en 순으로 처음 있는 이름.
    /// </summary>
    /// <param name="namesByLang">lang_code별 이름.</param>
    /// <returns>어느 언어에도 이름이 없으면 null.</returns>
    public static string? Pick(IReadOnlyDictionary<string, string?> namesByLang, string? langCode)
    {
        foreach (var lang in FallbackOrder(langCode))
        {
            if (namesByLang.TryGetValue(lang, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>요청 언어 → ko → en (중복 제외).</summary>
    public static IEnumerable<string> FallbackOrder(string? langCode) =>
        new[] { LanguageCode.Normalize(langCode), LanguageCode.Ko, LanguageCode.En }.Distinct();
}

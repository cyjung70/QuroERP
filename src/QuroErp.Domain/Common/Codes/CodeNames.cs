using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Common.Codes;

/// <summary>Names of a Major or Minor code by language (sys_code_*_translation, SD-BR-901, SD-BR-911).</summary>
public static class CodeNames
{
    /// <summary>Trims the names and drops blank ones (no translation in that language). The en name is required.</summary>
    /// <exception cref="DomainException">errorCode with the code as its arg when there is no en name.</exception>
    public static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string?>? names, string errorCode, string code)
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (lang, name) in names ?? new Dictionary<string, string?>())
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                kept[LanguageCode.Normalize(lang) ?? ""] = name.Trim();
            }
        }
        if (!kept.ContainsKey(LanguageCode.Fallback))
        {
            throw new DomainException(errorCode, code);
        }
        return kept;
    }
}

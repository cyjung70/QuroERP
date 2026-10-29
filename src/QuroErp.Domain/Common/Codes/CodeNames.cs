using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Common.Codes;

/// <summary>Names of a Major or Minor code by language (sys_code_*_translation, SD-BR-901, SD-BR-911, BC-BR-405).</summary>
public static class CodeNames
{
    /// <summary>
    /// The name changes a save sends, by normalized language code: trimmed text sets the name, null (a blank name) removes that
    /// language's translation. Languages that are not sent keep their names (BC-BR-405). The en name is required.
    /// </summary>
    /// <exception cref="DomainException">errorCode with the code as its arg when there is no en name.</exception>
    public static IReadOnlyDictionary<string, string?> Normalize(IReadOnlyDictionary<string, string?>? names, string errorCode, string code)
    {
        var sent = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (lang, name) in names ?? new Dictionary<string, string?>())
        {
            sent[LanguageCode.Normalize(lang) ?? ""] = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        if (sent.GetValueOrDefault(LanguageCode.Fallback) is null)
        {
            throw new DomainException(errorCode, code);
        }
        return sent;
    }
}

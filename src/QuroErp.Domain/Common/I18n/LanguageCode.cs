using System.Text.RegularExpressions;

namespace QuroErp.Domain.Common.I18n;

/// <summary>
/// Language codes (i18n.md, BC-BR-401). The list of languages is the sys_language master; en, ko and sw are seeded.
/// </summary>
public static partial class LanguageCode
{
    public const string En = "en";
    public const string Ko = "ko";
    public const string Sw = "sw";

    /// <summary>Last fallback language: every resource key and master name exists in en (i18n.md).</summary>
    public const string Fallback = En;

    /// <summary>Invalid language code or en set to not in use. Arg: language code.</summary>
    public const string InvalidCode = "BC-E-401";

    public static IReadOnlyList<string> Seeded { get; } = [En, Ko, Sw];

    /// <summary>
    /// The primary language subtag in lower case ("en-US" → "en"). Null when the value is empty.
    /// Whether the language is in use is checked against sys_language, not here.
    /// </summary>
    public static string? Normalize(string? code)
    {
        var language = code?.Split('-', '_')[0].Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(language) ? null : language;
    }

    /// <summary>BCP 47 primary language subtag: 2 or 3 lower-case letters.</summary>
    public static bool IsValid(string? code) => code is not null && Pattern().IsMatch(code);

    /// <exception cref="DomainException">BC-E-401 when the code is not valid.</exception>
    public static string Ensure(string? code) => IsValid(code) ? code! : throw new DomainException(InvalidCode, code ?? "");

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex Pattern();
}

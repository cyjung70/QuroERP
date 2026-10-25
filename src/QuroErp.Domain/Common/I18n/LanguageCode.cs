namespace QuroErp.Domain.Common.I18n;

/// <summary>지원 언어 (i18n.md): ko(기본), en.</summary>
public static class LanguageCode
{
    public const string Ko = "ko";
    public const string En = "en";

    public static IReadOnlyList<string> Supported { get; } = [Ko, En];

    /// <summary>"en-US"처럼 지역이 붙은 값은 언어 부분만 본다. 지원하지 않는 언어는 기본 언어(ko)로 본다.</summary>
    public static string Normalize(string? code)
    {
        var language = code?.Split('-', '_')[0].Trim().ToLowerInvariant();
        return language is En ? En : Ko;
    }
}

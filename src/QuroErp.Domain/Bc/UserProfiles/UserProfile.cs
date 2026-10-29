using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.UserProfiles;

/// <summary>Values a user may change in my info (BC-BR-102). Language and warehouse use are checked by the caller.</summary>
public sealed record UserProfileFields(string? UserName, string? Email, string? Phone, string? LangCode, long? DefaultWarehouseId);

/// <summary>My info rules BC-BR-102 and BC-BR-403.</summary>
public static partial class UserProfile
{
    /// <summary>A value breaks the my info field rules. Arg: property name.</summary>
    public const string InvalidFieldCode = "BC-E-102";

    /// <summary>The language is not in use. Arg: language code.</summary>
    public const string LanguageNotInUseCode = "BC-E-403";

    public const int UserNameMaxLength = 40;
    public const int EmailMaxLength = 30;
    public const int PhoneMaxLength = 18;

    /// <summary>
    /// Trims text and turns blank text into null, then checks name (required, 40), email (30, format when entered)
    /// and phone (18). A blank language follows the browser language [추정]; a blank warehouse is the company default.
    /// </summary>
    /// <exception cref="DomainException">BC-E-102 with the property name.</exception>
    public static UserProfileFields Normalize(UserProfileFields fields)
    {
        var normalized = new UserProfileFields(
            Clean(fields.UserName), Clean(fields.Email), Clean(fields.Phone), Clean(fields.LangCode)?.ToLowerInvariant(), fields.DefaultWarehouseId);

        if (normalized.UserName is null || normalized.UserName.Length > UserNameMaxLength)
        {
            throw new DomainException(InvalidFieldCode, nameof(UserProfileFields.UserName));
        }
        if (normalized.Email is { } email && (email.Length > EmailMaxLength || !EmailFormat().IsMatch(email)))
        {
            throw new DomainException(InvalidFieldCode, nameof(UserProfileFields.Email));
        }
        if (normalized.Phone is { Length: > PhoneMaxLength })
        {
            throw new DomainException(InvalidFieldCode, nameof(UserProfileFields.Phone));
        }
        return normalized;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // One @, no spaces, and a dot inside the domain part
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex EmailFormat();
}

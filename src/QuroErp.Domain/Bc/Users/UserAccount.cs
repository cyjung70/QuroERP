using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Bc.Users;

/// <summary>Values the administrator saves for a user (BC-BR-122, 126, 403). Role and language use are checked by the caller.</summary>
public sealed record UserAccountFields(
    string? UserName, long? RoleId, string? Email, string? Phone, string? LangCode, DateOnly? UserValidDt, string ValidFlag);

/// <summary>User management rules BC-BR-121, 122, 125, 126 and 131 (docs/business-rules/BC/user.md).</summary>
public static class UserAccount
{
    /// <summary>The user ID is already used (BC-BR-121). Arg: user ID.</summary>
    public const string DuplicateUserIdCode = "BC-E-121";

    /// <summary>A value breaks the user field rules (BC-BR-121, 122). Arg: property name.</summary>
    public const string InvalidFieldCode = "BC-E-122";

    /// <summary>The administrator tried to stop using their own account or change their own role (BC-BR-131).</summary>
    public const string SelfChangeCode = "BC-E-131";

    /// <summary>Property names used as error args.</summary>
    public const string UserIdField = "UserId";
    public const string RoleIdField = "RoleId";

    /// <summary>BC-BR-121: required, trimmed and lowercased, at most 50 characters.</summary>
    /// <exception cref="DomainException">BC-E-122 with UserId.</exception>
    public static string NormalizeUserId(string? userId)
    {
        var normalized = string.IsNullOrWhiteSpace(userId) ? "" : AppUser.NormalizeUserId(userId);
        if (normalized.Length is 0 or > AppUser.MaxUserIdLength)
        {
            throw new DomainException(InvalidFieldCode, UserIdField);
        }
        return normalized;
    }

    /// <summary>
    /// BC-BR-122: name (required, 40) and role are required; email (30) is checked only when entered; phone (18).
    /// The limits are the my info ones (same columns, BC-BR-102). A blank valid flag is Y; a blank end date means no end (BC-BR-126).
    /// </summary>
    /// <exception cref="DomainException">BC-E-122 with the property name.</exception>
    public static UserAccountFields Normalize(UserAccountFields fields)
    {
        UserProfileFields profile;
        try
        {
            profile = UserProfile.Normalize(new UserProfileFields(fields.UserName, fields.Email, fields.Phone, fields.LangCode, null));
        }
        catch (DomainException e) when (e.Code == UserProfile.InvalidFieldCode)
        {
            throw new DomainException(InvalidFieldCode, [.. e.Args]);
        }
        if (fields.RoleId is null)
        {
            throw new DomainException(InvalidFieldCode, RoleIdField);
        }
        var validFlag = string.IsNullOrWhiteSpace(fields.ValidFlag) ? AppUser.ValidFlagUsed : fields.ValidFlag.Trim().ToUpperInvariant();
        return new UserAccountFields(profile.UserName, fields.RoleId, profile.Email, profile.Phone, profile.LangCode, fields.UserValidDt, validFlag);
    }

    /// <summary>BC-BR-131: an administrator may not stop using their own account or change their own role.</summary>
    /// <exception cref="DomainException">BC-E-131.</exception>
    public static void EnsureSelfChangeAllowed(bool isSelf, long? currentRoleId, UserAccountFields fields)
    {
        if (isSelf && (fields.RoleId != currentRoleId || fields.ValidFlag != AppUser.ValidFlagUsed))
        {
            throw new DomainException(SelfChangeCode);
        }
    }

    /// <summary>
    /// BC-BR-125: leaving "in use" (Y) revokes all the user's auto-login tokens and raises the token version,
    /// so the user's sessions end at the next refresh.
    /// </summary>
    public static bool StopsUse(string? currentValidFlag, UserAccountFields fields) =>
        currentValidFlag == AppUser.ValidFlagUsed && fields.ValidFlag != AppUser.ValidFlagUsed;
}

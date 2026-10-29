namespace QuroErp.Domain.Common.Auth;

/// <summary>New password rules (BC-BR-053~056) with the policy values of the authentication setting.</summary>
public static class PasswordPolicy
{
    /// <summary>
    /// The first broken rule in the order of BC-BR-059: length → character kinds → reuse → same as user ID.
    /// The current password check (AUTH-015) comes before this and is the caller's.
    /// </summary>
    /// <param name="isReused">Whether the password is the current or a recent one. Called only when the earlier rules pass,
    /// because it compares slow hashes.</param>
    /// <returns>Null when the password is allowed, otherwise the code and its message arguments.</returns>
    public static PasswordViolation? FirstViolation(string newPassword, string userId, AuthSetting setting, Func<bool> isReused)
    {
        if (newPassword.Length < setting.PasswordMinLength || newPassword.Length > AppUser.MaxPasswordLength)
        {
            return new PasswordViolation(AuthCodes.PasswordLength, setting.PasswordMinLength);
        }
        if (Categories(newPassword) < setting.PasswordRequiredCategories)
        {
            return new PasswordViolation(AuthCodes.PasswordCategories, setting.PasswordRequiredCategories);
        }
        if (isReused())
        {
            return new PasswordViolation(AuthCodes.PasswordReused);
        }
        if (string.Equals(newPassword, userId, StringComparison.OrdinalIgnoreCase))
        {
            return new PasswordViolation(AuthCodes.PasswordSameAsUserId);
        }
        return null;
    }

    /// <summary>
    /// Kinds used among upper-case letters, lower-case letters, digits and symbols (A-Z, a-z, 0-9).
    /// [추정] Any other character, including a space or a non-ASCII letter, counts as a symbol.
    /// </summary>
    public static int Categories(string password)
    {
        bool upper = false, lower = false, digit = false, symbol = false;
        foreach (var c in password)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                upper = true;
            }
            else if (char.IsAsciiLetterLower(c))
            {
                lower = true;
            }
            else if (char.IsAsciiDigit(c))
            {
                digit = true;
            }
            else
            {
                symbol = true;
            }
        }
        return (upper ? 1 : 0) + (lower ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0);
    }
}

/// <summary>A broken password rule: an AUTH code and its message arguments.</summary>
public sealed record PasswordViolation(string Code, params object[] Args);

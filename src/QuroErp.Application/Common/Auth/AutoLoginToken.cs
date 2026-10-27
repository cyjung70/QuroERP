using System.Security.Cryptography;

namespace QuroErp.Application.Common.Auth;

/// <summary>
/// "Keep me signed in" token. The raw token goes only to the client; the server stores its SHA-256 hash
/// (sys_auto_login_token.token_hash, 64 hex characters).
/// </summary>
public static class AutoLoginToken
{
    /// <summary>A new random token (256 bits, hex).</summary>
    public static string Create() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // Same SHA-256 hex as API keys: both are long random values, so no salt is needed
    public static string Hash(string token) => ApiKeyHash.Compute(token);
}

/// <summary>A stored auto-login token (sys_auto_login_token).</summary>
public sealed record AutoLoginTokenRow(long Id, long AppUserId, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt);

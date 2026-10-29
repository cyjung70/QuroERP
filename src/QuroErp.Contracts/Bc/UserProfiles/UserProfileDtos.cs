namespace QuroErp.Contracts.Bc.UserProfiles;

/// <summary>My info API routes (BC-UC-003). The target is always the signed-in user, so no route has an ID (BC-BR-101).</summary>
public static class UserProfileRoutes
{
    /// <summary>GET my info, PUT to change it. There is no POST or DELETE (BC-BR-104).</summary>
    public const string Base = "api/bc/user-profile";

    /// <summary>POST: revokes all my auto-login tokens and keeps this session (BC-BR-106). 204.</summary>
    public const string RevokeTokens = Base + "/revoke-tokens";
}

/// <summary>
/// The signed-in user's info. UserId is read-only. DefaultWarehouseId null means the company default warehouse;
/// DefaultWarehouseCode and DefaultWarehouseName show the warehouse in use either way.
/// </summary>
public sealed record UserProfileDto(
    string UserId,
    string? UserName,
    string? Email,
    string? Phone,
    string? LangCode,
    long? DefaultWarehouseId,
    string? DefaultWarehouseCode,
    string? DefaultWarehouseName,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The only values my info may change (BC-BR-102). There is no password here (BC-BR-103).
/// UpdatedAt is the value from GET; a different stored value is 409 (BC-BR-105).
/// </summary>
public sealed record UpdateUserProfileRequest(
    string? UserName,
    string? Email,
    string? Phone,
    string? LangCode,
    long? DefaultWarehouseId,
    DateTimeOffset UpdatedAt);

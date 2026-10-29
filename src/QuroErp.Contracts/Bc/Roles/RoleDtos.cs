namespace QuroErp.Contracts.Bc.Roles;

/// <summary>Role API routes (BC-UC role-menu).</summary>
public static class RoleRoutes
{
    /// <summary>GET: roles in use, for any signed-in user (role select fields such as user management).</summary>
    public const string Base = "api/sys/roles";
}

/// <summary>One role in use.</summary>
public sealed record RoleDto(long Id, string RoleCode);

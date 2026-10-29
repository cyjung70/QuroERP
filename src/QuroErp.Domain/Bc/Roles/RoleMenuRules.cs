using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Roles;

/// <summary>Role menu permission rules (BC-BR-211~216).</summary>
public static class RoleMenuRules
{
    /// <summary>The role is missing or not in use (BC-BR-211). Answered with 400.</summary>
    public const string NoRoleCode = "BC-E-211";

    /// <summary>The menu is already assigned to the role, or added twice. Arg: menu id (BC-BR-212).</summary>
    public const string DuplicateMenuCode = "BC-E-212";

    /// <summary>The save would leave no role in use with Full Access to the role menu screen, or take it from my own role (BC-BR-216).</summary>
    public const string LastAdminCode = "BC-E-216";

    /// <summary>
    /// The role menu screen, found by its program id because menu codes come from the loaded menu data
    /// (BC-BR-216; decided for TASK-BC-009).
    /// </summary>
    public const string RoleMenuProgramId = "/bc/role-menu";

    public const string FullAccessYes = "Y";
    public const string FullAccessNo = "N";

    public static string ToFlag(bool fullAccess) => fullAccess ? FullAccessYes : FullAccessNo;

    public static bool IsFullAccess(string? flag) => string.Equals(flag, FullAccessYes, StringComparison.OrdinalIgnoreCase);

    /// <summary>BC-BR-212: a menu is assigned to a role once. Added menus must not be assigned already or added twice.</summary>
    public static void EnsureNoDuplicates(IReadOnlySet<long> assignedMenuIds, IEnumerable<long> addedMenuIds)
    {
        var seen = new HashSet<long>();
        foreach (var menuId in addedMenuIds)
        {
            if (assignedMenuIds.Contains(menuId) || !seen.Add(menuId))
            {
                throw new DomainException(DuplicateMenuCode, menuId);
            }
        }
    }

    /// <summary>
    /// BC-BR-216, given the roles in use that have Full Access to the role menu screen before and after the save.
    /// When no role had it before (menus just loaded), there is nothing to keep yet.
    /// </summary>
    public static void EnsureRoleMenuAccessKept(IReadOnlySet<long> rolesBefore, IReadOnlySet<long> rolesAfter, long? myRoleId)
    {
        if (rolesBefore.Count > 0 && rolesAfter.Count == 0)
        {
            throw new DomainException(LastAdminCode);
        }
        if (myRoleId is { } mine && rolesBefore.Contains(mine) && !rolesAfter.Contains(mine))
        {
            throw new DomainException(LastAdminCode);
        }
    }
}

using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Menus;

/// <summary>Menu fields after normalizing. Type and module are codes; ParentMenuCode is empty for a top-level menu.</summary>
public sealed record MenuFields(string Name, string Type, string? ProgramId, string Module, string? ParentMenuCode, string ValidFlag);

/// <summary>A menu in the tree that a save would leave: code, type and parent code.</summary>
public sealed record MenuNode(string MenuCode, string Type, string? ParentMenuCode);

/// <summary>Company menu rules (BC-BR-201~207). Every error names the menu code of the failing row.</summary>
public static class MenuRules
{
    public const string InvalidCodeCode = "BC-E-201";
    public const string InvalidNameCode = "BC-E-202";
    public const string InvalidTypeCode = "BC-E-203";
    public const string InvalidModuleCode = "BC-E-204";
    public const string InvalidParentCode = "BC-E-205";
    public const string InUseCode = "BC-E-207";

    public const int MaxCodeLength = 10;
    public const int MaxNameLength = 30;

    public const string ProgramType = "P";
    public const string PopupType = "U";
    public const string GroupType = "M";

    public const string InUse = "Y";

    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal) { ProgramType, PopupType, GroupType };

    public static readonly IReadOnlySet<string> Modules = new HashSet<string>(StringComparer.Ordinal) { "bc", "mm", "sd", "fi", "rpt" };

    /// <summary>BC-BR-201: required, upper case, at most 10 characters.</summary>
    public static string NormalizeCode(string? menuCode)
    {
        var code = menuCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
        {
            throw new DomainException(InvalidCodeCode, menuCode ?? "");
        }
        return code;
    }

    /// <summary>BC-BR-202~204 for one row. A blank valid flag means in use; the flag code itself is checked against VALID_FLAG by the service.</summary>
    public static MenuFields Normalize(
        string menuCode, string? name, string? type, string? programId, string? module, string? parentMenuCode, string? validFlag)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
        {
            throw new DomainException(InvalidNameCode, menuCode);
        }

        var menuType = type?.Trim().ToUpperInvariant() ?? "";
        var program = string.IsNullOrWhiteSpace(programId) ? null : programId.Trim();
        // P and U open a screen, so they need its path; M only groups other menus
        if (!Types.Contains(menuType) || (menuType == GroupType) != (program is null))
        {
            throw new DomainException(InvalidTypeCode, menuCode);
        }

        var menuModule = module?.Trim().ToLowerInvariant() ?? "";
        if (!Modules.Contains(menuModule))
        {
            throw new DomainException(InvalidModuleCode, menuCode);
        }

        var parent = string.IsNullOrWhiteSpace(parentMenuCode) ? null : parentMenuCode.Trim().ToUpperInvariant();
        var flag = string.IsNullOrWhiteSpace(validFlag) ? InUse : validFlag.Trim().ToUpperInvariant();
        return new MenuFields(trimmedName, menuType, program, menuModule, parent, flag);
    }

    /// <summary>
    /// BC-BR-205 for the changed menus in the tree the save would leave: the parent is empty or an M menu,
    /// and following parents never comes back to the menu itself.
    /// </summary>
    public static void EnsureParents(IReadOnlyCollection<MenuNode> tree, IEnumerable<string> changedCodes)
    {
        var byCode = tree.ToDictionary(n => n.MenuCode, StringComparer.Ordinal);
        foreach (var code in changedCodes)
        {
            var node = byCode[code];
            if (node.ParentMenuCode is null)
            {
                continue;
            }
            if (!byCode.TryGetValue(node.ParentMenuCode, out var parent) || parent.Type != GroupType)
            {
                throw new DomainException(InvalidParentCode, code);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal) { code };
            for (var up = parent; up is not null; up = up.ParentMenuCode is { } next ? byCode.GetValueOrDefault(next) : null)
            {
                if (!seen.Add(up.MenuCode))
                {
                    throw new DomainException(InvalidParentCode, code);
                }
            }
        }
    }

    /// <summary>BC-BR-207: a menu assigned to a role, or one that still has child menus, is set not in use instead of deleted.</summary>
    public static void EnsureDeletable(string menuCode, bool assignedToRole, IReadOnlyCollection<MenuNode> tree)
    {
        if (assignedToRole || tree.Any(n => n.ParentMenuCode == menuCode))
        {
            throw new DomainException(InUseCode, menuCode);
        }
    }
}

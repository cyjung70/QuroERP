using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Components.Layout;

/// <summary>A side menu link: a P menu with a screen path (app-shell.md 3, 4).</summary>
public sealed record ShellNavLink(string MenuCode, string? Name, string Path, bool Current);

/// <summary>An entry in a group, in menu code order: a link, or a subheading (an M menu inside the group) with its links.</summary>
public sealed record ShellNavEntry(ShellNavLink? Link, string? HeadingCode, string? HeadingName, IReadOnlyList<ShellNavLink> HeadingLinks);

/// <summary>A side menu group: a top-level M menu. Current when it holds the current link.</summary>
public sealed record ShellNavGroup(string MenuCode, string? Name, IReadOnlyList<ShellNavEntry> Entries, bool Current);

/// <summary>
/// Builds the side menu from the /me menus (app-shell.md 3): top-level M menus are groups, P menus under them are links,
/// M menus inside a group are subheadings (two levels only), U menus are not shown. Groups and subheadings with no link are left
/// out. The current link is the one whose path is the current path or a parent of it; the longest path wins.
/// </summary>
public static class ShellMenuTree
{
    private const string GroupType = "M";
    private const string ProgramType = "P";

    public static IReadOnlyList<ShellNavGroup> Build(IEnumerable<AuthMenu> menus, string currentPath)
    {
        var all = menus.ToList();
        var current = CurrentPath(all, currentPath);

        ShellNavLink? Link(AuthMenu m) =>
            m.Type == ProgramType && !string.IsNullOrWhiteSpace(m.ProgramId)
                ? new ShellNavLink(m.MenuCode, m.Name, m.ProgramId,
                    string.Equals(m.ProgramId.TrimEnd('/'), current, StringComparison.OrdinalIgnoreCase))
                : null;

        IEnumerable<AuthMenu> Children(string parent) =>
            all.Where(m => m.ParentMenuCode == parent).OrderBy(m => m.MenuCode, StringComparer.Ordinal);

        var groups = new List<ShellNavGroup>();
        foreach (var group in all.Where(m => m.Type == GroupType && m.ParentMenuCode is null).OrderBy(m => m.MenuCode, StringComparer.Ordinal))
        {
            var entries = new List<ShellNavEntry>();
            foreach (var child in Children(group.MenuCode))
            {
                if (child.Type == GroupType)
                {
                    var links = Children(child.MenuCode).Select(Link).OfType<ShellNavLink>().ToList();
                    if (links.Count > 0)
                    {
                        entries.Add(new ShellNavEntry(null, child.MenuCode, child.Name, links));
                    }
                }
                else if (Link(child) is { } link)
                {
                    entries.Add(new ShellNavEntry(link, null, null, []));
                }
            }
            if (entries.Count > 0)
            {
                var hasCurrent = entries.Any(e => e.Link?.Current == true || e.HeadingLinks.Any(l => l.Current));
                groups.Add(new ShellNavGroup(group.MenuCode, group.Name, entries, hasCurrent));
            }
        }
        return groups;
    }

    // The program path that is the current path or its longest parent (/mm/purchase-order for /mm/purchase-order/12)
    private static string? CurrentPath(IEnumerable<AuthMenu> menus, string currentPath)
    {
        var path = currentPath.Split('?', '#')[0].TrimEnd('/');
        return menus
            .Where(m => m.Type == ProgramType && !string.IsNullOrWhiteSpace(m.ProgramId))
            .Select(m => m.ProgramId!.TrimEnd('/'))
            .Where(p => p.Length > 0 && (path.Equals(p, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.Length)
            .FirstOrDefault();
    }
}

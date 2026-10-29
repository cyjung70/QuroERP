using System.Collections.Concurrent;
using QuroErp.Application.Common.Persistence;

namespace QuroErp.Application.Bc.Roles;

/// <summary>What a role may do with a menu (BC-BR-214).</summary>
public enum MenuAccess
{
    None,
    Read,
    Full,
}

/// <summary>
/// Menu permission lookup for the "Menu:&lt;menu_code&gt;" API policy (security.md "권한", BC-BR-214). Permissions are cached per role;
/// saving a role's menus clears that role's entry, so the change applies from the next request (BC-BR-215).
/// The cache lives in this API process: with more than one API instance, a change reaches the other instances only when they restart. [추정]
/// </summary>
public sealed class MenuPermissionService(IRoleRepository roles, IUnitOfWorkFactory unitOfWork)
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, bool>> _cache = new(StringComparer.Ordinal);

    public async Task<MenuAccess> GetAccessAsync(string roleCode, string menuCode, CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(roleCode, out var menus))
        {
            await using var uow = await unitOfWork.BeginAsync(cancellationToken);
            var permissions = await roles.FindPermissionsAsync(roleCode, uow.Transaction, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            menus = permissions.ToDictionary(p => p.MenuCode, p => p.FullAccess, StringComparer.Ordinal);
            _cache[roleCode] = menus;
        }
        return menus.TryGetValue(menuCode, out var full) ? full ? MenuAccess.Full : MenuAccess.Read : MenuAccess.None;
    }

    /// <summary>BC-BR-215: call after a role's menus are saved.</summary>
    public void Invalidate(string roleCode) => _cache.TryRemove(roleCode, out _);

    /// <summary>BC-BR-206, BC-BR-215: call after menus change, since every role may hold them.</summary>
    public void InvalidateAll() => _cache.Clear();
}

using System.Data;
using QuroErp.Application.Bc.Menus;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Bc.Menus;

namespace QuroErp.Api.Tests.Bc.Menus;

/// <summary>Company menus BASE (group), ITEM and USER under it; ITEM is assigned to a role.</summary>
public sealed class InMemoryMenuRepository : IMenuRepository
{
    public const long BaseMenuId = 1;
    public const long ItemMenuId = 2;
    public const long UserMenuId = 3;

    public static readonly DateTimeOffset Created = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, (string Code, MenuFields Fields, long? ParentId, DateTimeOffset UpdatedAt)> _rows = new()
    {
        [BaseMenuId] = ("BASE", new MenuFields("Base", "M", null, "bc", null, "Y"), null, Created),
        [ItemMenuId] = ("ITEM", new MenuFields("Items", "P", "/bc/item", "bc", null, "Y"), BaseMenuId, Created),
        [UserMenuId] = ("USER", new MenuFields("Users", "P", "/bc/user", "bc", null, "Y"), BaseMenuId, Created),
    };
    private readonly HashSet<long> _assigned = [ItemMenuId];
    private long _nextId = 100;

    public Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MenuDto>>(_rows
                .Where(r => menuCode is null || r.Value.Code.StartsWith(menuCode, StringComparison.Ordinal))
                .OrderBy(r => r.Value.Code, StringComparer.Ordinal)
                .Select(r => new MenuDto(r.Key, r.Value.Code, r.Value.Fields.Name, r.Value.Fields.Type, r.Value.Fields.ProgramId, r.Value.Fields.Module,
                    r.Value.ParentId is { } p ? _rows[p].Code : null, r.Value.Fields.ValidFlag, r.Value.UpdatedAt))
                .ToList());
        }
    }

    public Task<IReadOnlyList<MenuRecord>> LockAllAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MenuRecord>>(
                _rows.Select(r => new MenuRecord(r.Key, r.Value.Code, r.Value.Fields.Type, r.Value.ParentId, r.Value.UpdatedAt)).ToList());
        }
    }

    public Task<IReadOnlySet<long>> FindAssignedAsync(IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(menuIds.Where(_assigned.Contains).ToHashSet());

    public Task<long> InsertAsync(string menuCode, MenuFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = (menuCode, fields, null, Created.AddMinutes(1));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, MenuFields fields, long? parentMenuId, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(id, out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = (row.Code, fields, parentMenuId, row.UpdatedAt.AddMinutes(1));
            return Task.FromResult(true);
        }
    }

    public Task SetParentAsync(long id, long? parentMenuId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows[id] = _rows[id] with { ParentId = parentMenuId };
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                _rows.Remove(id);
            }
            return Task.CompletedTask;
        }
    }
}

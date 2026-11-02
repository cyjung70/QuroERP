using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Items;

/// <summary>Alternate item groups (BC-UC-019, MM-BR-810, MM-BR-813, MM-BR-814): search, and a grid save in one transaction.</summary>
public sealed class AlternateItemGroupService(IAlternateItemGroupRepository groups, ICodeValidator codes, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>A changed or deleted group does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <exception cref="DomainException">Common-E-002 for an item class that is not an active ITEM_CLASS code.</exception>
    public async Task<IReadOnlyList<AlternateItemGroupRowDto>> SearchAsync(AlternateItemGroupSearchQuery query, CancellationToken cancellationToken)
    {
        var itemClass = Clean(query.ItemClass);
        if (itemClass is not null)
        {
            await codes.EnsureValidAsync(CodeMajors.ItemClass, itemClass, cancellationToken);
        }
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await groups.SearchAsync(itemClass, Clean(query.Keyword), AlternateItemGroupRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>Deletes, changes and adds the rows together and returns the groups of the item class; the first bad row stops the save.</summary>
    /// <exception cref="DomainException">422 MM-E-802 without an item class, Common-E-002, MM-E-810 (arg: code).</exception>
    /// <exception cref="ConflictException">409 MM-E-814 (arg: code) for a deleted group that has items.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row's updatedAt differs from the stored one.</exception>
    /// <exception cref="NotFoundException">Common-E-006 for an unknown id.</exception>
    public async Task<IReadOnlyList<AlternateItemGroupRowDto>> SaveAsync(
        SaveAlternateItemGroupsRequest request, string user, CancellationToken cancellationToken)
    {
        var itemClass = Clean(request.ItemClass) ?? throw new DomainException(Item.RequiredCode, nameof(request.ItemClass));
        // MM-BR-813: rows are checked in grid order, added rows first
        var added = (request.Added ?? []).Select(r => AlternateItemGroupRules.Normalize(r.AltItemGroupCd, r.AltItemGroupName)).ToList();
        var updatedRows = request.Updated ?? [];
        var updated = updatedRows.Select(r => (r.Id, Fields: AlternateItemGroupRules.Normalize(r.AltItemGroupCd, r.AltItemGroupName))).ToList();
        var deletedRows = request.Deleted ?? [];
        await codes.EnsureValidAsync(CodeMajors.ItemClass, itemClass, cancellationToken);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await groups.LockAsync(uow.Transaction, cancellationToken);
        var expected = updatedRows.Select(r => (r.Id, r.UpdatedAt)).Concat(deletedRows.Select(r => (r.Id, r.UpdatedAt))).ToList();
        var stored = (await groups.FindForUpdateAsync(expected.Select(e => e.Id).Distinct().ToList(), uow.Transaction, cancellationToken))
            .ToDictionary(g => g.Id);
        foreach (var (id, updatedAt) in expected)
        {
            var row = stored.GetValueOrDefault(id) ?? throw new NotFoundException(NotFoundCode, id);
            if (row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }
        if (deletedRows.Select(r => stored[r.Id]).FirstOrDefault(g => g.ItemCount > 0) is { } inUse)
        {
            throw new ConflictException(AlternateItemGroupRules.InUseCode, inUse.AltItemGroupCd);
        }

        var rowCodes = added.Select(a => a.AltItemGroupCd).Concat(updated.Select(u => u.Fields.AltItemGroupCd)).ToList();
        var touched = stored.Keys.ToHashSet();
        var owners = rowCodes.Count == 0 ? [] : await groups.FindCodeOwnersAsync(rowCodes.Distinct().ToList(), uow.Transaction, cancellationToken);
        AlternateItemGroupRules.EnsureUnique(rowCodes, owners.Where(o => !touched.Contains(o.Id)).Select(o => o.Code).ToHashSet(StringComparer.Ordinal));

        if (deletedRows.Count > 0)
        {
            await groups.DeleteAsync(deletedRows.Select(r => r.Id).Distinct().ToList(), uow.Transaction, cancellationToken);
        }
        if (updated.Count > 0)
        {
            await groups.UpdateAsync(updated, user, uow.Transaction, cancellationToken);
        }
        if (added.Count > 0)
        {
            await groups.InsertAsync(added, itemClass, user, uow.Transaction, cancellationToken);
        }
        var rows = await groups.SearchAsync(itemClass, null, AlternateItemGroupRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    private static AlternateItemGroupRowDto ToDto(AlternateItemGroupRow r) =>
        new(r.Id, r.AltItemGroupCd, r.AltItemGroupName, r.ItemClass, r.ItemCount, r.UpdatedAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

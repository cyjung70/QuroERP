using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.Items;

/// <summary>Alternate items (BC-UC-012, MM-BR-811~813): the items of one group, and a grid save in one transaction.</summary>
public sealed class AlternateItemService(IAlternateItemRepository repository, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The group does not exist. Arg: group code or id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <exception cref="DomainException">MM-E-802 without a group code.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<AlternateItemGroupDto> GetAsync(AlternateItemQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.GroupCode))
        {
            throw new DomainException(Item.RequiredCode, nameof(AlternateItemQuery.GroupCode));
        }
        var code = query.GroupCode.Trim();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var group = await repository.FindGroupByCodeAsync(code, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, code);
        var dto = await ReadAsync(group, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves the added, changed and deleted rows together; the first bad row stops the save (MM-BR-813).</summary>
    /// <exception cref="DomainException">422 MM-E-802 for a row without an item code; MM-E-812 (arg: item code).</exception>
    /// <exception cref="ConflictException">409 MM-E-812 (args: item code, other group code) for a move that is not confirmed.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when updatedAt differs, or a removed item is no longer in the group.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<AlternateItemGroupDto> SaveAsync(SaveAlternateItemsRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var updated = (request.Updated ?? []).Select(u => new AlternateItemRowChange(u.ItemId, u.ItemCd)).ToList();
        var joinCodes = AlternateItemRules.JoinCodes(request.Added ?? [], updated);
        var leaving = (request.Deleted ?? []).Concat(updated.Select(u => u.ItemId)).ToList();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var group = await repository.FindGroupAsync(request.GroupId, forUpdate: true, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, request.GroupId);
        if (group.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var members = (await repository.FindMembersAsync(group.Id, uow.Transaction, cancellationToken)).Select(m => m.Id).ToHashSet();
        if (!leaving.All(members.Contains))
        {
            throw new ConcurrencyException();
        }
        IReadOnlyList<AlternateItemCandidate> candidates = joinCodes.Count == 0
            ? []
            : await repository.FindCandidatesAsync(joinCodes.Distinct().ToList(), uow.Transaction, cancellationToken);
        var plan = AlternateItemRules.Plan(
            group.Id, members, leaving, joinCodes, candidates.ToDictionary(c => c.ItemCd, StringComparer.Ordinal));
        if (!request.MoveConfirmed && plan.Moved.Count > 0)
        {
            throw new ConflictException(AlternateItemRules.InvalidItemCode, plan.Moved[0].ItemCd, plan.Moved[0].GroupCd ?? string.Empty);
        }
        await repository.SaveAsync(group.Id, plan.JoinIds, plan.LeaveIds, updatedBy, uow.Transaction, cancellationToken);
        var saved = await repository.FindGroupAsync(group.Id, forUpdate: false, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, group.Id);
        var dto = await ReadAsync(saved, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    private async Task<AlternateItemGroupDto> ReadAsync(AlternateItemGroupRecord group, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var members = await repository.FindMembersAsync(group.Id, transaction, cancellationToken);
        return new AlternateItemGroupDto(
            group.Id, group.AltItemGroupCd, group.AltItemGroupName, [.. members.Select(m => new AlternateItemDto(m.Id, m.ItemCd, m.ItemName))],
            group.UpdatedAt);
    }
}

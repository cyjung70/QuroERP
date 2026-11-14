using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SetItems;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.SetItems;

/// <summary>Set item composition (BC-UC-013, SD-BR-501~508, MM-BR-361).</summary>
public sealed class SetItemService(ISetItemRepository sets, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The set and its components in sequence order. Copying another set (SD-BR-508) reads it here.</summary>
    /// <exception cref="DomainException">Common-E-005 without a code, SD-E-501 when the item is not a Kit or Set in use.</exception>
    public async Task<SetItemDto> GetAsync(string? itemCd, CancellationToken cancellationToken)
    {
        var code = SetItemRules.NormalizeCode(itemCd, "itemCd");
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var set = SetItemRules.EnsureSet(code, await sets.FindItemAsync(code, forUpdate: false, uow.Transaction, cancellationToken));
        var dto = await ReadAsync(set, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Saves added, changed and deleted component rows of one set in one transaction (SD-BR-506). One failing row saves nothing;
    /// its item code is the message arg. New rows get the next sequences (SD-BR-505). Returns the set after the save.
    /// </summary>
    /// <exception cref="DomainException">Common-E-005, SD-E-501, SD-E-502, SD-E-503, BC-E-423, SD-E-504.</exception>
    /// <exception cref="ConcurrencyException">A changed or deleted row is gone, is not in this set, or was saved by someone else first.</exception>
    public async Task<SetItemDto> SaveAsync(
        string? itemCd, BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> request, string user,
        CancellationToken cancellationToken)
    {
        var code = SetItemRules.NormalizeCode(itemCd, "itemCd");
        IReadOnlyList<SetComponentRowRequest> added = request.Added ?? [];
        IReadOnlyList<SetComponentRowRequest> updated = request.Updated ?? [];
        IReadOnlyList<SetComponentDeleteRequest> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var set = SetItemRules.EnsureSet(code, await sets.FindItemAsync(code, forUpdate: true, uow.Transaction, cancellationToken));
        var stored = (await sets.FindComponentsAsync(set.Id, uow.Transaction, cancellationToken)).ToDictionary(r => r.Id);

        // Rows the save names must still be in this set as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt ?? default)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var rows = updated.Concat(added).Select(r => (r.Id, ItemCd: SetItemRules.NormalizeCode(r.ItemCd, "itemCd"), r.Qty)).ToList();
        var items = (await sets.FindItemsAsync([.. rows.Select(r => r.ItemCd).Distinct()], uow.Transaction, cancellationToken))
            .ToDictionary(i => i.ItemCd, StringComparer.Ordinal);
        var decimals = await DecimalsAsync(uow.Transaction, cancellationToken);
        foreach (var row in rows)
        {
            SetItemRules.EnsureComponent(row.ItemCd, items.GetValueOrDefault(row.ItemCd));
            SetItemRules.EnsureQty(row.ItemCd, row.Qty, decimals);
        }
        var touched = updated.Select(u => u.Id!.Value).Concat(deleted.Select(d => d.Id)).ToHashSet();
        SetItemRules.EnsureUnique(stored.Values.Where(r => !touched.Contains(r.Id)).Select(r => r.ItemCd).Concat(rows.Select(r => r.ItemCd)));

        // Deletes first; the component key is checked at commit, so changed rows may swap components (0405)
        foreach (var row in deleted)
        {
            if (!await sets.DeleteAsync(row.Id, row.UpdatedAt, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in updated)
        {
            var componentId = items[SetItemRules.NormalizeCode(row.ItemCd, "itemCd")].Id;
            if (!await sets.UpdateAsync(row.Id!.Value, componentId, row.Qty, row.UpdatedAt!.Value, user, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        var seqs = SetItemRules.NextSeqs(stored.Count == 0 ? 0 : stored.Values.Max(r => r.Seq), added.Count);
        foreach (var (row, seq) in added.Zip(seqs))
        {
            var componentId = items[SetItemRules.NormalizeCode(row.ItemCd, "itemCd")].Id;
            await sets.InsertAsync(set.Id, seq, componentId, row.Qty, user, uow.Transaction, cancellationToken);
        }

        var dto = await ReadAsync(set, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    private async Task<SetItemDto> ReadAsync(SetItemCandidate set, IDbTransaction transaction, CancellationToken cancellationToken) =>
        new(set.Id, set.ItemCd, set.ItemName, (await sets.FindComponentsAsync(set.Id, transaction, cancellationToken))
            .Select(r => new SetComponentDto(r.Id, r.Seq, r.ItemId, r.ItemCd, r.ItemName, r.Unit, r.Qty, r.UpdatedAt))
            .ToList());

    // Quantity decimals of the SD module (BC-BR-423); the country only matters for unit prices
    private async Task<IDecimalPolicy> DecimalsAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(transaction, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(transaction, cancellationToken);
        return new DecimalPolicy(
            company?.CountryCode ?? CountryProfiles.Us,
            settings.Select(s => new DecimalSetting(s.Module, s.QtyDecimals, Rounding.ParseMode(s.RoundingMode))),
            countryDecimals.Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            new Dictionary<string, string?>());
    }
}

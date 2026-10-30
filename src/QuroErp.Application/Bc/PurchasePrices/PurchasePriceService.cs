using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.PurchasePrices;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.PurchasePrices;

/// <summary>Purchase price table by price group (BC-UC-015, MM-BR-901~905).</summary>
public sealed class PurchasePriceService(
    IPurchasePriceRepository prices, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>MM-BR-905: the group's rows in item code order, each with the item's standard purchase price.</summary>
    /// <exception cref="DomainException">Common-E-005 or MM-E-901 for the price group.</exception>
    public async Task<IReadOnlyList<PurchasePriceDto>> SearchAsync(string? vendorPrice, CancellationToken cancellationToken)
    {
        var group = PurchasePriceRules.NormalizeGroup(vendorPrice);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(group, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows of one price group in one transaction (MM-BR-904). One failing row saves nothing; its
    /// item code is the message arg. Returns the group's rows after the save.
    /// </summary>
    /// <exception cref="DomainException">Common-E-005, MM-E-901, MM-E-902, MM-E-903, BC-E-423.</exception>
    /// <exception cref="ConcurrencyException">A changed or deleted row is gone or was saved by someone else first.</exception>
    public async Task<IReadOnlyList<PurchasePriceDto>> SaveAsync(
        string? vendorPrice, BatchRequest<PurchasePriceRowRequest, PurchasePriceDeleteRequest> request, string user,
        CancellationToken cancellationToken)
    {
        var group = PurchasePriceRules.NormalizeGroup(vendorPrice);
        IReadOnlyList<PurchasePriceRowRequest> added = request.Added ?? [];
        IReadOnlyList<PurchasePriceRowRequest> updated = request.Updated ?? [];
        IReadOnlyList<PurchasePriceDeleteRequest> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await prices.LockGroupAsync(group, uow.Transaction, cancellationToken);
        var stored = (await prices.FindByGroupAsync(group, uow.Transaction, cancellationToken)).ToDictionary(r => r.Id);

        // Rows the save names must still be in this group as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt ?? default)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var rows = updated.Concat(added).Select(r => (r.Id, ItemCd: Required(r.ItemCd), r.PurchasePrc)).ToList();
        var items = (await prices.FindItemsAsync([.. rows.Select(r => r.ItemCd).Distinct()], uow.Transaction, cancellationToken))
            .ToDictionary(i => i.ItemCd, StringComparer.Ordinal);
        var decimals = await DecimalsAsync(uow.Transaction, cancellationToken);
        foreach (var row in rows)
        {
            PurchasePriceRules.EnsureItem(row.ItemCd, items.GetValueOrDefault(row.ItemCd));
            PurchasePriceRules.EnsurePrice(row.ItemCd, row.PurchasePrc, decimals);
        }
        var touched = updated.Select(u => u.Id!.Value).Concat(deleted.Select(d => d.Id)).ToHashSet();
        PurchasePriceRules.EnsureUnique(
            group, stored.Values.Where(r => !touched.Contains(r.Id)).Select(r => r.ItemCd).Concat(rows.Select(r => r.ItemCd)));

        // Deletes first, so a deleted row's item can be added again in the same save
        foreach (var row in deleted)
        {
            if (!await prices.DeleteAsync(row.Id, row.UpdatedAt, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in updated)
        {
            var write = new PurchasePriceWrite(row.Id, items[row.ItemCd!.Trim()].Id, row.PurchasePrc);
            if (!await prices.UpdateAsync(write, row.UpdatedAt!.Value, user, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in added)
        {
            await prices.InsertAsync(
                group, new PurchasePriceWrite(null, items[row.ItemCd!.Trim()].Id, row.PurchasePrc), user, uow.Transaction, cancellationToken);
        }

        var list = await ReadAsync(group, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    private async Task<IReadOnlyList<PurchasePriceDto>> ReadAsync(string group, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await prices.FindByGroupAsync(group, transaction, cancellationToken))
        .Select(r => new PurchasePriceDto(r.Id, r.ItemId, r.ItemCd, r.ItemName, r.PurchasePrc, r.StdPrc, r.UpdatedAt))
        .ToList();

    // Purchase prices are in the base currency, so unit price decimals come from the company country row (BC-BR-429)
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

    private static string Required(string? itemCd) =>
        string.IsNullOrWhiteSpace(itemCd) ? throw new DomainException(PurchasePriceRules.RequiredCode, "itemCd") : itemCd.Trim();
}

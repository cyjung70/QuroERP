using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SalesPrices;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.SalesPrices;

/// <summary>Sales price table by price level (BC-UC-014, SD-BR-403, SD-BR-407~409).</summary>
public sealed class SalesPriceService(
    ISalesPriceRepository prices, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>SD-BR-409: the level's rows in item code order, each with the item's standard sales price.</summary>
    /// <exception cref="DomainException">Common-E-005 or SD-E-403 for the price level.</exception>
    public async Task<IReadOnlyList<SalesPriceDto>> SearchAsync(string? priceLevel, CancellationToken cancellationToken)
    {
        var level = SalesPriceRules.NormalizeLevel(priceLevel);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(level, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows of one price level in one transaction (SD-BR-409). One failing row saves nothing; its
    /// item code is the message arg. Returns the level's rows after the save.
    /// </summary>
    /// <exception cref="DomainException">Common-E-005, SD-E-403, SD-E-408, SD-E-407, BC-E-423.</exception>
    /// <exception cref="ConcurrencyException">A changed or deleted row is gone or was saved by someone else first.</exception>
    public async Task<IReadOnlyList<SalesPriceDto>> SaveAsync(
        string? priceLevel, BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> request, string user,
        CancellationToken cancellationToken)
    {
        var level = SalesPriceRules.NormalizeLevel(priceLevel);
        IReadOnlyList<SalesPriceRowRequest> added = request.Added ?? [];
        IReadOnlyList<SalesPriceRowRequest> updated = request.Updated ?? [];
        IReadOnlyList<SalesPriceDeleteRequest> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await prices.LockLevelAsync(level, uow.Transaction, cancellationToken);
        var stored = (await prices.FindByLevelAsync(level, uow.Transaction, cancellationToken)).ToDictionary(r => r.Id);

        // Rows the save names must still be in this level as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt ?? default)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var rows = updated.Concat(added).Select(r => (r.Id, ItemCd: Required(r.ItemCd), r.SalesPrc)).ToList();
        var items = (await prices.FindItemsAsync([.. rows.Select(r => r.ItemCd).Distinct()], uow.Transaction, cancellationToken))
            .ToDictionary(i => i.ItemCd, StringComparer.Ordinal);
        var decimals = await DecimalsAsync(uow.Transaction, cancellationToken);
        foreach (var row in rows)
        {
            SalesPriceRules.EnsureItem(row.ItemCd, items.GetValueOrDefault(row.ItemCd));
            SalesPriceRules.EnsurePrice(row.ItemCd, row.SalesPrc, decimals);
        }
        var touched = updated.Select(u => u.Id!.Value).Concat(deleted.Select(d => d.Id)).ToHashSet();
        SalesPriceRules.EnsureUnique(
            level, stored.Values.Where(r => !touched.Contains(r.Id)).Select(r => r.ItemCd).Concat(rows.Select(r => r.ItemCd)));

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
            var write = new SalesPriceWrite(row.Id, items[row.ItemCd!.Trim()].Id, row.SalesPrc);
            if (!await prices.UpdateAsync(write, row.UpdatedAt!.Value, user, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in added)
        {
            await prices.InsertAsync(
                level, new SalesPriceWrite(null, items[row.ItemCd!.Trim()].Id, row.SalesPrc), user, uow.Transaction, cancellationToken);
        }

        var list = await ReadAsync(level, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    private async Task<IReadOnlyList<SalesPriceDto>> ReadAsync(string level, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await prices.FindByLevelAsync(level, transaction, cancellationToken))
        .Select(r => new SalesPriceDto(r.Id, r.ItemId, r.ItemCd, r.ItemName, r.SalesPrc, r.StdPrc, r.UpdatedAt))
        .ToList();

    // Sales prices are in the base currency, so unit price decimals come from the company country row (BC-BR-429)
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
        string.IsNullOrWhiteSpace(itemCd) ? throw new DomainException(SalesPriceRules.RequiredCode, "itemCd") : itemCd.Trim();
}

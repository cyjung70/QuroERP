using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Currency management (BC-UC-026, BC-BR-404, BC-BR-411, BC-BR-416, BC-BR-428, BC-BR-312).</summary>
public sealed class CurrencyService(
    ICurrencyRepository currencies, ITranslationRepository translations, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>A name is in a language that is not in use. Arg: the language code.</summary>
    public const string LanguageNotInUseCode = "BC-E-405";

    /// <summary>Every currency in code order with names in langCode; useYn filters by the in-use flag when given.</summary>
    public async Task<IReadOnlyList<CurrencyMasterDto>> SearchAsync(string? langCode, bool? useYn, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return useYn is null ? list : [.. list.Where(c => c.UseYn == useYn)];
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction. One failing row saves nothing; its currency code is the message arg.
    /// Returns every currency after the save, with names in langCode.
    /// </summary>
    /// <exception cref="DomainException">422 BC-E-411, BC-E-428, BC-E-404, BC-E-405 or BC-E-312.</exception>
    /// <exception cref="ConflictException">
    /// 409 BC-E-411 or BC-E-428 when documents use a currency whose decimals or country change; 409 BC-E-416 when a deleted currency is
    /// the base currency or documents or partners use it.
    /// </exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row changed after it was read.</exception>
    public async Task<IReadOnlyList<CurrencyMasterDto>> SaveAsync(
        BatchRequest<CurrencySaveRow, CurrencyKey> request, string? langCode, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<CurrencySaveRow> added = request.Added ?? [];
        IReadOnlyList<CurrencySaveRow> updated = request.Updated ?? [];
        IReadOnlyList<CurrencyKey> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await currencies.FindAllAsync(forUpdate: true, uow.Transaction, cancellationToken)).ToDictionary(c => c.Id);

        // Rows the save names must still be as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, (DateTimeOffset?)d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var newRows = added.Select(row =>
        {
            var code = CurrencyRules.NormalizeCode(row.CurrencyCode);
            return (Code: code, Fields: CurrencyRules.Normalize(code, row.Decimals, row.CountryCode, row.UseYn, row.Names));
        }).ToList();
        var changedRows = updated.Select(row =>
        {
            var current = stored[row.Id!.Value];
            CurrencyRules.EnsureSameCode(current.CurrencyCode, row.CurrencyCode);
            return (current.Id, Code: current.CurrencyCode,
                Fields: CurrencyRules.Normalize(current.CurrencyCode, row.Decimals ?? current.Decimals, row.CountryCode, row.UseYn, row.Names));
        }).ToList();

        var languages = (await localization.FindLanguagesAsync(uow.Transaction, cancellationToken))
            .Where(l => l.UseYn).Select(l => l.LangCode).ToHashSet(StringComparer.Ordinal);
        if (newRows.Concat(changedRows.Select(r => (r.Code, r.Fields))).SelectMany(r => r.Fields.Names.Keys)
                .FirstOrDefault(lang => !languages.Contains(lang)) is { } unknown)
        {
            throw new DomainException(LanguageNotInUseCode, unknown);
        }

        // The currencies the save leaves: stored rows minus deleted, plus new rows
        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        CurrencyRules.EnsureUnique(stored.Values.Where(c => !deletedIds.Contains(c.Id)).Select(c => c.CurrencyCode).Concat(newRows.Select(r => r.Code)));

        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        foreach (var (_, code, fields) in changedRows)
        {
            CurrencyRules.EnsureBaseStaysInUse(code, fields.UseYn, company?.BaseCurrencyCode);
        }
        var locked = changedRows
            .Select(r => (r.Code, Error: CurrencyRules.LockedByDocuments(
                new StoredCurrency(r.Code, stored[r.Id].Decimals, stored[r.Id].CountryCode), r.Fields)))
            .Where(r => r.Error is not null)
            .ToList();
        if (locked.Count > 0)
        {
            var used = await currencies.FindUsedByDocumentsAsync([.. locked.Select(r => r.Code)], uow.Transaction, cancellationToken);
            if (locked.FirstOrDefault(r => used.Contains(r.Code)) is { Error: not null } blocked)
            {
                throw new ConflictException(blocked.Error, blocked.Code);
            }
        }

        // BC-BR-416: checked before anything is deleted, so one blocked row saves nothing
        var deletedCodes = deleted.Select(d => stored[d.Id].CurrencyCode).ToList();
        if (deletedCodes.Count > 0)
        {
            var used = (await currencies.FindUsedByDocumentsAsync(deletedCodes, uow.Transaction, cancellationToken))
                .Union(await currencies.FindUsedByPartnersAsync(deletedCodes, uow.Transaction, cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            if (CurrencyRules.FirstUndeletable(deletedCodes, company?.BaseCurrencyCode, used) is { } undeletable)
            {
                throw new ConflictException(CurrencyRules.InUseCode, undeletable);
            }
        }

        // Deletes first: a new row may reuse a deleted code
        await currencies.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var (id, _, fields) in changedRows)
        {
            if (!await currencies.UpdateAsync(id, fields, stored[id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
            await currencies.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var (code, fields) in newRows)
        {
            var id = await currencies.InsertAsync(code, fields, updatedBy, uow.Transaction, cancellationToken);
            await currencies.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await ReadAsync(langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    private async Task<IReadOnlyList<CurrencyMasterDto>> ReadAsync(string? langCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await currencies.FindAllAsync(forUpdate: false, transaction, cancellationToken);
        var names = (await translations.FindAsync(TranslationTarget.CurrencyName, rows.Select(r => r.Id).ToList(), transaction, cancellationToken))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToLookup(t => t.OwnerId);
        var used = await currencies.FindUsedByDocumentsAsync([.. rows.Select(r => r.CurrencyCode)], transaction, cancellationToken);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        return rows.Select(r =>
        {
            var byLang = names[r.Id].ToDictionary(t => t.LangCode, t => t.Name!, StringComparer.Ordinal);
            var name = Translation.Pick(byLang.ToDictionary(n => n.Key, n => (string?)n.Value), langCode, company?.DefaultLangCode);
            return new CurrencyMasterDto(r.Id, r.CurrencyCode, name, byLang, r.Decimals, r.CountryCode, r.UseYn, used.Contains(r.CurrencyCode), r.UpdatedAt);
        }).ToList();
    }
}

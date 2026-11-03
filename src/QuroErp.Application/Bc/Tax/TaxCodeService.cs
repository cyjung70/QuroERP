using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Application.Bc.Tax;

/// <summary>Tax code management (BC-UC-028, BC-BR-404, BC-BR-501~506).</summary>
public sealed class TaxCodeService(
    ITaxCodeRepository taxCodes, ITranslationRepository translations, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Every tax code in code order with names in langCode; useYn and taxKind filter when given.</summary>
    public async Task<IReadOnlyList<TaxCodeDto>> SearchAsync(string? langCode, bool? useYn, string? taxKind, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        var kind = taxKind?.Trim().ToUpperInvariant();
        return [.. list.Where(t => (useYn is null || t.UseYn == useYn) && (string.IsNullOrEmpty(kind) || t.TaxKind == kind))];
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction. One failing row saves nothing; its tax code is the message arg.
    /// Returns every tax code after the save, with names in langCode.
    /// </summary>
    /// <exception cref="DomainException">422 BC-E-501, BC-E-502, BC-E-503, BC-E-504, BC-E-404 or BC-E-405.</exception>
    /// <exception cref="ConflictException">
    /// 409 BC-E-505 when NONE is deleted, renamed, given another kind or set not in use; 409 BC-E-506 when documents use a code whose
    /// kind, rate or tax area changes, or a deleted code is used by documents or partners.
    /// </exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row changed after it was read.</exception>
    public async Task<IReadOnlyList<TaxCodeDto>> SaveAsync(
        BatchRequest<TaxCodeSaveRow, TaxCodeKey> request, string? langCode, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<TaxCodeSaveRow> added = request.Added ?? [];
        IReadOnlyList<TaxCodeSaveRow> updated = request.Updated ?? [];
        IReadOnlyList<TaxCodeKey> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await taxCodes.FindAllAsync(forUpdate: true, uow.Transaction, cancellationToken)).ToDictionary(t => t.Id);

        // Rows the save names must still be as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, (DateTimeOffset?)d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        var countryKinds = CountryProfiles.Get(company?.CountryCode ?? CountryProfiles.Us).TaxKinds;
        var areaIds = added.Concat(updated).Select(r => r.TaxAreaId).OfType<long>().Distinct().ToList();
        var knownAreas = await taxCodes.FindTaxAreasAsync(areaIds, uow.Transaction, cancellationToken);

        TaxCodeFields Normalize(string code, TaxCodeSaveRow row) => TaxCodeRules.Normalize(
            code, row.TaxKind, row.Rate, row.TaxAreaId, row.PriceIncluded, row.UseYn, row.Names, countryKinds, knownAreas);

        var newRows = added.Select(row =>
        {
            var code = TaxCodeRules.NormalizeCode(row.TaxCode);
            return (Code: code, Fields: Normalize(code, row));
        }).ToList();
        var changedRows = updated.Select(row =>
        {
            var current = stored[row.Id!.Value];
            var code = string.IsNullOrWhiteSpace(row.TaxCode) ? current.TaxCode : TaxCodeRules.NormalizeCode(row.TaxCode);
            return (current.Id, Code: code, Fields: Normalize(code, row));
        }).ToList();

        var languages = (await localization.FindLanguagesAsync(uow.Transaction, cancellationToken))
            .Where(l => l.UseYn).Select(l => l.LangCode).ToHashSet(StringComparer.Ordinal);
        if (newRows.Concat(changedRows.Select(r => (r.Code, r.Fields))).SelectMany(r => r.Fields.Names.Keys)
                .FirstOrDefault(lang => !languages.Contains(lang)) is { } unknown)
        {
            throw new DomainException(CurrencyService.LanguageNotInUseCode, unknown);
        }

        // The codes the save leaves: stored rows minus deleted and changed, plus changed and new rows
        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        var changedIds = changedRows.Select(r => r.Id).ToHashSet();
        TaxCodeRules.EnsureUnique(stored.Values.Where(t => !deletedIds.Contains(t.Id) && !changedIds.Contains(t.Id)).Select(t => t.TaxCode)
            .Concat(changedRows.Select(r => r.Code)).Concat(newRows.Select(r => r.Code)));

        // BC-BR-505: NONE stays as it is
        if (changedRows.Any(r => TaxCodeRules.ChangesNone(stored[r.Id].TaxCode, r.Code, r.Fields))
            || deleted.Any(d => stored[d.Id].TaxCode == TaxCodeRules.NoneTaxCode))
        {
            throw new ConflictException(TaxCodeRules.NoneCode, TaxCodeRules.NoneTaxCode);
        }

        // BC-BR-506: documents keep the kind, rate and tax area of their code; used codes are not deleted
        var changing = changedRows
            .Where(r => TaxCodeRules.ChangesWhatDocumentsUse(Stored(stored[r.Id]), r.Fields))
            .Select(r => r.Id)
            .ToList();
        var usedByDocuments = await taxCodes.FindUsedByDocumentsAsync([.. changing.Concat(deletedIds)], uow.Transaction, cancellationToken);
        var usedByPartners = await taxCodes.FindUsedByPartnersAsync([.. deletedIds], uow.Transaction, cancellationToken);
        var blocked = changing.Where(usedByDocuments.Contains)
            .Concat(deleted.Select(d => d.Id).Where(id => usedByDocuments.Contains(id) || usedByPartners.Contains(id)))
            .ToList();
        if (blocked.Count > 0)
        {
            throw new ConflictException(TaxCodeRules.InUseCode, stored[blocked[0]].TaxCode);
        }

        // Deletes first: a new row may reuse a deleted code
        await taxCodes.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var (id, code, fields) in changedRows)
        {
            if (!await taxCodes.UpdateAsync(id, code, fields, stored[id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
            await taxCodes.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var (code, fields) in newRows)
        {
            var id = await taxCodes.InsertAsync(code, fields, updatedBy, uow.Transaction, cancellationToken);
            await taxCodes.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await ReadAsync(langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    private static StoredTaxCode Stored(TaxCodeRecord row) => new(row.TaxCode, row.TaxKind, row.Rate, row.TaxAreaId);

    private async Task<IReadOnlyList<TaxCodeDto>> ReadAsync(string? langCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await taxCodes.FindAllAsync(forUpdate: false, transaction, cancellationToken);
        var names = (await translations.FindAsync(TranslationTarget.TaxCodeName, rows.Select(r => r.Id).ToList(), transaction, cancellationToken))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToLookup(t => t.OwnerId);
        var used = await taxCodes.FindUsedByDocumentsAsync([.. rows.Select(r => r.Id)], transaction, cancellationToken);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        return rows.Select(r =>
        {
            var byLang = names[r.Id].ToDictionary(t => t.LangCode, t => t.Name!, StringComparer.Ordinal);
            var name = Translation.Pick(byLang.ToDictionary(n => n.Key, n => (string?)n.Value), langCode, company?.DefaultLangCode);
            return new TaxCodeDto(
                r.Id, r.TaxCode, name, byLang, r.TaxKind, r.Rate, r.TaxAreaId, r.TaxAreaCode, r.PriceIncluded, r.UseYn, used.Contains(r.Id), r.UpdatedAt);
        }).ToList();
    }
}

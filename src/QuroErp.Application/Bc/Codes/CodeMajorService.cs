using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Application.Bc.Codes;

/// <summary>Major code management (BC-UC-023, SD-BR-901~905).</summary>
public sealed class CodeMajorService(
    ICodeMajorRepository majors, ITranslationRepository translations, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Majors whose code starts with majorCode (every Major when blank), in code order, with names in langCode.</summary>
    public async Task<IReadOnlyList<CodeMajorDto>> SearchAsync(string? majorCode, string? langCode, CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(majorCode) ? null : majorCode.Trim().ToUpperInvariant();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(prefix, langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction (SD-BR-905). One failing row saves nothing; its Major code is the message
    /// arg. Returns every Major after the save, with names in langCode.
    /// </summary>
    public async Task<IReadOnlyList<CodeMajorDto>> SaveAsync(
        SaveCodeMajorsRequest request, string? langCode, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<CodeMajorInsert> inserted = request.Inserted ?? [];
        IReadOnlyList<CodeMajorUpdate> updated = request.Updated ?? [];
        IReadOnlyList<CodeMajorDelete> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await majors.FindAsync(null, forUpdate: true, uow.Transaction, cancellationToken)).ToDictionary(m => m.Id);

        // Rows the save names must still be there as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var newRows = inserted.Select(row =>
        {
            var code = CodeMajorRules.NormalizeCode(row.MajorCode);
            return (Code: code, Fields: CodeMajorRules.Normalize(code, row.Names, row.UseYn));
        }).ToList();
        var changedRows = updated.Select(row =>
        {
            var code = stored[row.Id].MajorCode;
            CodeMajorRules.EnsureSameCode(code, row.MajorCode);
            return (row.Id, Code: code, Fields: CodeMajorRules.Normalize(code, row.Names, row.UseYn));
        }).ToList();

        var languages = (await localization.FindLanguagesAsync(uow.Transaction, cancellationToken))
            .Where(l => l.UseYn).Select(l => l.LangCode).ToHashSet(StringComparer.Ordinal);
        if (newRows.Select(r => r.Fields).Concat(changedRows.Select(r => r.Fields)).SelectMany(f => f.Names.Keys)
                .FirstOrDefault(lang => !languages.Contains(lang)) is { } unknown)
        {
            throw new DomainException(CodeMinorService.LanguageNotInUseCode, unknown);
        }

        // The Major codes the save leaves: stored rows, minus deleted, plus new rows
        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        CodeMajorRules.EnsureUnique(stored.Values.Where(m => !deletedIds.Contains(m.Id)).Select(m => m.MajorCode)
            .Concat(newRows.Select(r => r.Code))
            .ToList());

        foreach (var row in changedRows.Where(r => !r.Fields.UseYn))
        {
            CodeMajorRules.EnsureNotProtected(row.Code);
        }
        foreach (var id in deletedIds)
        {
            CodeMajorRules.EnsureDeletable(stored[id].MajorCode, stored[id].MinorCount);
        }

        // Deletes first: a new row may reuse a deleted code
        await majors.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var (id, _, fields) in changedRows)
        {
            if (!await majors.UpdateAsync(id, fields.UseYn, stored[id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
            await majors.ReplaceNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var (code, fields) in newRows)
        {
            var id = await majors.InsertAsync(code, fields.UseYn, updatedBy, uow.Transaction, cancellationToken);
            await majors.ReplaceNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await ReadAsync(null, langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    private async Task<IReadOnlyList<CodeMajorDto>> ReadAsync(
        string? codePrefix, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await majors.FindAsync(codePrefix, forUpdate: false, transaction, cancellationToken);
        var names = (await translations.FindAsync(TranslationTarget.CodeMajor, rows.Select(r => r.Id).ToList(), transaction, cancellationToken))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToLookup(t => t.OwnerId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        return rows.Select(r =>
        {
            var byLang = names[r.Id].ToDictionary(t => t.LangCode, t => t.Name!, StringComparer.Ordinal);
            var name = Translation.Pick(byLang.ToDictionary(n => n.Key, n => (string?)n.Value), langCode, company?.DefaultLangCode);
            return new CodeMajorDto(r.Id, r.MajorCode, name, byLang, r.UseYn, r.MinorCount, r.UpdatedAt);
        }).ToList();
    }
}

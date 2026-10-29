using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Application.Bc.Codes;

/// <summary>Minor code management (BC-UC-008, SD-BR-905, SD-BR-910~916).</summary>
public sealed class CodeMinorService(
    ICodeMinorRepository minors, ITranslationRepository translations, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Required input is empty. Arg: the property.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>A name is in a language that is not in use. Arg: the language code.</summary>
    public const string LanguageNotInUseCode = "BC-E-405";

    /// <summary>SD-BR-910: every Minor code of the Major in sort order, in use or not, with names in langCode.</summary>
    /// <exception cref="BadRequestException">Common-E-005 when majorCode is empty.</exception>
    /// <exception cref="NotFoundException">Common-E-003 when the Major does not exist.</exception>
    public async Task<IReadOnlyList<CodeMinorDto>> SearchAsync(string? majorCode, string? langCode, CancellationToken cancellationToken)
    {
        var major = NormalizeMajor(majorCode);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var majorId = await minors.FindMajorIdAsync(major, forUpdate: false, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CodeMessages.UnknownMajor, major);
        var list = await ReadAsync(majorId, langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows of one Major in one transaction (SD-BR-905). One failing row saves nothing; its Minor code
    /// is the message arg. Returns every Minor code of the Major after the save, with names in langCode.
    /// </summary>
    public async Task<IReadOnlyList<CodeMinorDto>> SaveAsync(
        SaveCodeMinorsRequest request, string? langCode, string updatedBy, CancellationToken cancellationToken)
    {
        var major = NormalizeMajor(request.MajorCode);
        IReadOnlyList<CodeMinorInsert> inserted = request.Inserted ?? [];
        IReadOnlyList<CodeMinorUpdate> updated = request.Updated ?? [];
        IReadOnlyList<CodeMinorDelete> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var majorId = await minors.FindMajorIdAsync(major, forUpdate: true, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CodeMessages.UnknownMajor, major);
        var stored = (await minors.FindByMajorAsync(majorId, uow.Transaction, cancellationToken)).ToDictionary(m => m.Id);

        // Rows the save names must still be in this Major as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var newRows = inserted.Select(row =>
        {
            var code = CodeMinorRules.NormalizeCode(row.MinorCode);
            return (Code: code, Fields: CodeMinorRules.Normalize(code, row.Names, row.SortOrder, row.UseYn, row.Attr1, row.Attr2));
        }).ToList();
        var changedRows = updated.Select(row =>
        {
            var code = stored[row.Id].MinorCode;
            CodeMinorRules.EnsureSameCode(code, row.MinorCode);
            return (row.Id, Code: code, Fields: CodeMinorRules.Normalize(code, row.Names, row.SortOrder, row.UseYn, row.Attr1, row.Attr2));
        }).ToList();

        var languages = (await localization.FindLanguagesAsync(uow.Transaction, cancellationToken))
            .Where(l => l.UseYn).Select(l => l.LangCode).ToHashSet(StringComparer.Ordinal);
        if (newRows.Concat(changedRows.Select(r => (r.Code, r.Fields))).SelectMany(r => r.Fields.Names.Keys)
                .FirstOrDefault(lang => !languages.Contains(lang)) is { } unknown)
        {
            throw new DomainException(LanguageNotInUseCode, unknown);
        }

        // The Minor codes the save leaves: stored rows, minus deleted, with changed rows replaced and new rows added
        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        var changedById = changedRows.ToDictionary(r => r.Id);
        CodeMinorRules.EnsureUnique(stored.Values.Where(m => !deletedIds.Contains(m.Id))
            .Select(m => new CodeMinorNode(m.MinorCode, changedById.TryGetValue(m.Id, out var changed) ? changed.Fields.SortOrder : m.SortOrder))
            .Concat(newRows.Select(r => new CodeMinorNode(r.Code, r.Fields.SortOrder)))
            .ToList());

        foreach (var row in changedRows.Where(r => !r.Fields.UseYn))
        {
            CodeMinorRules.EnsureNotProtected(major, row.Code);
        }
        var deletedCodes = deletedIds.Select(id => stored[id].MinorCode).ToList();
        var used = await minors.FindUsedAsync(major, deletedCodes, uow.Transaction, cancellationToken);
        foreach (var code in deletedCodes)
        {
            CodeMinorRules.EnsureDeletable(major, code, used.Contains(code));
        }

        // Deletes first: a new row may reuse a deleted code
        await minors.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var (id, _, fields) in changedRows)
        {
            if (!await minors.UpdateAsync(id, fields, stored[id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
            await minors.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var (code, fields) in newRows)
        {
            var id = await minors.InsertAsync(majorId, code, fields, updatedBy, uow.Transaction, cancellationToken);
            await minors.SaveNamesAsync(id, fields.Names, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await ReadAsync(majorId, langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    private async Task<IReadOnlyList<CodeMinorDto>> ReadAsync(
        long majorId, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await minors.FindByMajorAsync(majorId, transaction, cancellationToken);
        var names = (await translations.FindAsync(TranslationTarget.CodeMinor, rows.Select(r => r.Id).ToList(), transaction, cancellationToken))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToLookup(t => t.OwnerId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        return rows.Select(r =>
        {
            var byLang = names[r.Id].ToDictionary(t => t.LangCode, t => t.Name!, StringComparer.Ordinal);
            var name = Translation.Pick(byLang.ToDictionary(n => n.Key, n => (string?)n.Value), langCode, company?.DefaultLangCode);
            return new CodeMinorDto(r.Id, r.MinorCode, name, byLang, r.SortOrder, r.UseYn, r.Attr1, r.Attr2, r.UpdatedAt);
        }).ToList();
    }

    private static string NormalizeMajor(string? majorCode) =>
        string.IsNullOrWhiteSpace(majorCode)
            ? throw new BadRequestException(RequiredCode, nameof(SaveCodeMinorsRequest.MajorCode))
            : majorCode.Trim().ToUpperInvariant();
}

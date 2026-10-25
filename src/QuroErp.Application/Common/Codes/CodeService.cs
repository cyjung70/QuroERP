using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Codes;

namespace QuroErp.Application.Common.Codes;

/// <summary>Code lists for combo boxes and radio buttons (codes.md "화면").</summary>
public sealed class CodeService(ICodeRepository codes, TranslationService translations, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>
    /// Active Minor codes of majorCode in sort_order, with names in langCode (ko → en fallback).
    /// A code without any translation uses its minor_code as the name.
    /// </summary>
    /// <exception cref="NotFoundException">Common-E-003 when the Major does not exist or is not in use.</exception>
    public async Task<IReadOnlyList<CodeItemDto>> GetCodesAsync(string majorCode, string? langCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var minors = await codes.FindActiveMinorsAsync(majorCode, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CodeMessages.UnknownMajor, majorCode);
        var names = await translations.GetNamesAsync(
            TranslationTarget.CodeMinor, minors.Select(m => m.Id).ToList(), langCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return minors.Select(m => new CodeItemDto(m.MinorCode, names.GetValueOrDefault(m.Id, m.MinorCode))).ToList();
    }
}

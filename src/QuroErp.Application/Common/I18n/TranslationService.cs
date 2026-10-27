using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Application.Common.I18n;

/// <summary>마스터 데이터 이름을 사용자 언어로 읽는다. 다른 서비스가 자기 트랜잭션 안에서 쓴다.</summary>
public sealed class TranslationService(ITranslationRepository translations, ILocalizationRepository localization)
{
    /// <summary>
    /// Reads the names of ownerIds in langCode. Without a translation the name falls back to the company
    /// default language, then en (Translation.Pick, BC-BR-402).
    /// </summary>
    /// <returns>owner id → 이름. 어느 언어에도 이름이 없는 id는 빠진다.</returns>
    public async Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        TranslationTarget target, IReadOnlyCollection<long> ownerIds, string? langCode,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (ownerIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        var rows = await translations.FindAsync(target, ownerIds, transaction, cancellationToken);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var names = new Dictionary<long, string>();
        foreach (var owner in rows.GroupBy(r => r.OwnerId))
        {
            var name = Translation.Pick(owner.ToDictionary(r => r.LangCode, r => r.Name), langCode, company?.DefaultLangCode);
            if (name is not null)
            {
                names[owner.Key] = name;
            }
        }
        return names;
    }
}

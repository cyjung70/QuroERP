using System.Data;

namespace QuroErp.Application.Common.I18n;

/// <summary>번역 테이블의 한 행. OwnerId는 번역하는 업무 행의 id (major_id, minor_id, item_id).</summary>
public sealed record TranslationRow(long OwnerId, string LangCode, string? Name);

public interface ITranslationRepository
{
    /// <summary>ownerIds의 모든 언어 번역을 읽는다.</summary>
    Task<IReadOnlyList<TranslationRow>> FindAsync(
        TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

using System.Data;

namespace QuroErp.Application.Common.Codes;

/// <summary>An active Minor code (sys_code_minor).</summary>
public sealed record CodeMinorRow(long Id, string MinorCode, int SortOrder);

public interface ICodeRepository
{
    /// <summary>Active Minor codes of the Major, ordered by sort_order.</summary>
    /// <returns>null when the Major does not exist or is not in use.</returns>
    Task<IReadOnlyList<CodeMinorRow>?> FindActiveMinorsAsync(string majorCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>True when both the Major and the Minor exist and are in use.</summary>
    Task<bool> ExistsActiveAsync(string majorCode, string minorCode, IDbTransaction transaction, CancellationToken cancellationToken);
}

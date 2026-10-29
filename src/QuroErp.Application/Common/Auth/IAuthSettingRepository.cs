using System.Data;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>The setting with the updated_at the edit screen sends back (BC-BR-304 style check).</summary>
public sealed record AuthSettingRow(AuthSetting Setting, DateTimeOffset UpdatedAt);

public interface IAuthSettingRepository
{
    /// <summary>The one setting row, or null if the row is missing (callers then use the defaults).</summary>
    Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The one setting row with its updated_at, or null if it is missing.</summary>
    /// <param name="forUpdate">Locks the row.</param>
    Task<AuthSettingRow?> FindForEditAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when the row is missing or its updated_at is not expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        AuthSetting setting, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);
}

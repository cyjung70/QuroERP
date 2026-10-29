using System.Data;
using QuroErp.Domain.Bc.UserProfiles;

namespace QuroErp.Application.Bc.UserProfiles;

/// <summary>A sys_app_user row as my info shows it, with the warehouse in use (the company default when the user has none).</summary>
public sealed record UserProfileRow(
    long Id,
    string UserId,
    string? UserName,
    string? Email,
    string? Phone,
    string? LangCode,
    long? DefaultWarehouseId,
    string? DefaultWarehouseCode,
    string? DefaultWarehouseName,
    DateTimeOffset UpdatedAt);

public interface IUserProfileRepository
{
    /// <param name="forUpdate">Locks the user row.</param>
    Task<UserProfileRow?> FindAsync(long appUserId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The language exists in sys_language and is in use (BC-BR-403).</summary>
    Task<bool> IsLanguageInUseAsync(string langCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The warehouse exists in bc_warehouse and is in use (BC-BR-102).</summary>
    Task<bool> IsWarehouseInUseAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the fields when updated_at still equals <paramref name="expectedUpdatedAt"/>.</summary>
    /// <returns>False when another save came first.</returns>
    Task<bool> UpdateAsync(
        long appUserId, UserProfileFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken);
}

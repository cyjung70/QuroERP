using System.Data;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.BusinessPlaces;

namespace QuroErp.Application.Bc.BusinessPlaces;

/// <summary>Business places (bc_business_place, BC-BR-319). The default one is made by the schema script.</summary>
public interface IBusinessPlaceRepository
{
    /// <summary>Default first, then by code.</summary>
    Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>null when the place does not exist.</returns>
    /// <param name="forUpdate">Locks the row.</param>
    Task<BusinessPlaceDto?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when the place is missing or its updated_at is not expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, BusinessPlaceFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>The default place new documents and warehouses get (BC-BR-321).</summary>
    /// <returns>null when there is no default place.</returns>
    Task<long?> FindDefaultIdAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}

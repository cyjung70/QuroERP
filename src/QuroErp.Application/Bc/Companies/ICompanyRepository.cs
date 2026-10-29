using System.Data;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Domain.Bc.Companies;

namespace QuroErp.Application.Bc.Companies;

/// <summary>Logo bytes and MIME type (bc_company.logo_image, logo_content_type).</summary>
public sealed record CompanyLogoRow(byte[] LogoImage, string LogoContentType);

/// <summary>
/// The single company (bc_company, BC-BR-301). When more than one row exists, the lowest id is the company.
/// </summary>
public interface ICompanyRepository
{
    /// <returns>null when there is no company.</returns>
    /// <param name="forUpdate">Locks the company row.</param>
    Task<CompanyDto?> FindAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when there is no company or its updated_at is not expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        CompanyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Whether any document, stock movement, payment or exchange rate exists (BC-BR-312, BC-BR-313).</summary>
    Task<bool> HasTransactionsAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Whether a shipment line that is not void has ship_qty &gt; billed_qty (BC-BR-316).</summary>
    Task<bool> HasUnbilledShipmentsAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>null when there is no company or it has no logo.</returns>
    Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when there is no company.</returns>
    Task<bool> UpdateLogoAsync(byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets logo_image and logo_content_type to null (BC-BR-306).</summary>
    /// <returns>false when there is no company.</returns>
    Task<bool> DeleteLogoAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);
}

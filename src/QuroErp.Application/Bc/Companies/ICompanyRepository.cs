using System.Data;

namespace QuroErp.Application.Bc.Companies;

/// <summary>Logo bytes and MIME type (bc_company.logo_image, logo_content_type).</summary>
public sealed record CompanyLogoRow(byte[] LogoImage, string LogoContentType);

/// <summary>
/// The single company (bc_company, ADR-0025). When more than one row exists, the lowest id is the company.
/// </summary>
public interface ICompanyRepository
{
    /// <returns>null when there is no company or it has no logo.</returns>
    Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>false when there is no company.</returns>
    Task<bool> UpdateLogoAsync(byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);
}

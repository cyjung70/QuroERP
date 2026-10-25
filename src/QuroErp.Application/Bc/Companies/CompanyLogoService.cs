using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Companies;

namespace QuroErp.Application.Bc.Companies;

/// <summary>Logo bytes with the MIME type to send back.</summary>
public sealed record CompanyLogoFile(byte[] Content, string ContentType);

/// <summary>Company logo for printed documents (BC-UC-007, ADR-0024).</summary>
public sealed class CompanyLogoService(ICompanyRepository companies, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The company does not exist yet.</summary>
    public const string CompanyNotFoundCode = "BC-E-006";

    /// <summary>No logo has been uploaded.</summary>
    public const string LogoNotFoundCode = "BC-E-007";

    /// <exception cref="NotFoundException">BC-E-007 when there is no logo.</exception>
    public async Task<CompanyLogoFile> GetLogoAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await companies.FindLogoAsync(uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(LogoNotFoundCode);
        await uow.CommitAsync(cancellationToken);
        return new CompanyLogoFile(row.LogoImage, row.LogoContentType);
    }

    /// <summary>Replaces the logo. The type is taken from the file content (PNG or JPEG).</summary>
    /// <exception cref="Domain.Common.DomainException">BC-E-004 (not PNG/JPEG), BC-E-005 (too large).</exception>
    /// <exception cref="NotFoundException">BC-E-006 when there is no company.</exception>
    public async Task UploadLogoAsync(byte[] image, string updatedBy, CancellationToken cancellationToken)
    {
        var logo = CompanyLogo.Create(image);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        if (!await companies.UpdateLogoAsync(logo.Image, logo.ContentType, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new NotFoundException(CompanyNotFoundCode);
        }
        await uow.CommitAsync(cancellationToken);
    }
}

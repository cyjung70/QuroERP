using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.BusinessPlaces;

namespace QuroErp.Application.Bc.BusinessPlaces;

/// <summary>
/// Business places on the company screen (BC-BR-319, BC-BR-320, docs/architecture/organization.md). The MVP has the default one
/// made at installation; it is only read and changed here.
/// </summary>
public sealed class BusinessPlaceService(
    IBusinessPlaceRepository places, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The business place does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await places.ListAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>Saves the place and returns it as stored.</summary>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    /// <exception cref="Domain.Common.DomainException">422 BC-E-320 with the property name, BC-E-319.</exception>
    /// <exception cref="ConcurrencyException">409 when the place changed after it was read.</exception>
    public async Task<BusinessPlaceDto> UpdateAsync(long id, UpdateBusinessPlaceRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var current = await places.FindAsync(id, true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        // BC-BR-320: the KR registration number check follows the company country
        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        var fields = BusinessPlaceRules.Normalize(
            new BusinessPlaceFields(
                request.BusinessPlaceName, request.BusinessRegNo, request.SubBusinessNo, request.CeoName, request.BusinessType,
                request.BusinessItem, request.Addr1, request.Addr2, request.City, request.State, request.ZipCode, request.TelNo, request.UseYn),
            company?.CountryCode, current.IsDefault);
        if (current.UpdatedAt != request.UpdatedAt
            || !await places.UpdateAsync(id, fields, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = (await places.FindAsync(id, false, uow.Transaction, cancellationToken))!;
        await uow.CommitAsync(cancellationToken);
        return saved;
    }
}

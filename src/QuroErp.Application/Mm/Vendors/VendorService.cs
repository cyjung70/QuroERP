using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Mm.Vendors;

namespace QuroErp.Application.Mm.Vendors;

/// <summary>The vendor master screen (MM-UC-001).</summary>
public sealed class VendorService(
    IVendorRepository vendors, ILocalizationRepository localization, ITaxCodeRepository taxCodes, ICodeValidator codes,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The vendor does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<VendorListDto> SearchAsync(VendorSearchQuery query, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(query.Size, 1, VendorRoutes.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var (rows, total) = await vendors.SearchAsync(
            new VendorSearch(query.VName, query.ValidFlag, (page - 1) * size, size), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new VendorListDto(rows.Select(r => ToDto(r)).ToList(), total);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<VendorDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await vendors.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>The vendor before or after the given one in name order (MM-UC-001 basic flow 1); null at either end.</summary>
    public async Task<VendorDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await vendors.FindAdjacentAsync(id, forward, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return row is null ? null : ToDto(row);
    }

    /// <exception cref="DomainException">
    /// MM-E-851, 852, 853, 856, 859, 872; BC-E-415, 521, 522, 523; Common-E-002 for an unknown code value.
    /// </exception>
    public async Task<VendorDto> CreateAsync(CreateVendorRequest request, string createdBy, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var country = await CountryAsync(uow.Transaction, cancellationToken);
        var vendor = Vendor.Create(request.VName, ToFields(request), country == CountryProfiles.Kr);
        if (await vendors.NameExistsAsync(vendor.VName, uow.Transaction, cancellationToken))
        {
            throw new DomainException(Vendor.DuplicateNameCode, vendor.VName);
        }
        var fields = await ResolveCurrencyAndTaxCodeAsync(vendor.Fields, country, uow.Transaction, cancellationToken);
        vendor = Vendor.Restore(0, vendor.VName, fields);
        var warnings = await WarningsAsync(vendor, null, country, uow.Transaction, cancellationToken);
        var id = await vendors.InsertAsync(vendor, createdBy, uow.Transaction, cancellationToken);
        var saved = await vendors.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!, warnings);
    }

    /// <exception cref="DomainException">
    /// MM-E-851, 853, 856, 859, 872; BC-E-415, 521, 522, 523; Common-E-002 for an unknown code value.
    /// </exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<VendorDto> UpdateAsync(long id, UpdateVendorRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await vendors.FindAsync(id, forUpdate: true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var country = await CountryAsync(uow.Transaction, cancellationToken);
        var vendor = Vendor.Restore(row.Id, row.VName, row.Fields);
        vendor.Change(ToFields(request), country == CountryProfiles.Kr);
        vendor = Vendor.Restore(row.Id, row.VName, await ResolveCurrencyAndTaxCodeAsync(vendor.Fields, country, uow.Transaction, cancellationToken));
        var warnings = await WarningsAsync(vendor, id, country, uow.Transaction, cancellationToken);
        if (!await vendors.UpdateAsync(vendor, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = await vendors.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!, warnings);
    }

    /// <exception cref="DomainException">MM-E-857 when the vendor is used.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await vendors.FindAsync(id, forUpdate: true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != updatedAt)
        {
            throw new ConcurrencyException();
        }
        Vendor.Restore(row.Id, row.VName, row.Fields).EnsureDeletable(await vendors.IsInUseAsync(id, uow.Transaction, cancellationToken));
        if (!await vendors.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    // MM-BR-855
    private async Task EnsureCodesAsync(VendorFieldsRequest r, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.Terms, Blank(r.Terms), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ValidFlag, Blank(r.ValidFlag), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Blank(r.State), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Blank(r.ShipState), cancellationToken);
    }

    private async Task<string> CountryAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await localization.FindCompanyAsync(transaction, cancellationToken))?.CountryCode ?? CountryProfiles.Us;

    // BC-BR-415: a currency in use, empty for the base currency. BC-BR-523: a tax code in use, empty for the country default
    private async Task<VendorFields> ResolveCurrencyAndTaxCodeAsync(
        VendorFields fields, string country, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (fields.CurrencyCode is { } currency
            && !(await localization.FindCurrenciesAsync(transaction, cancellationToken)).Any(c => c.UseYn && c.CurrencyCode == currency))
        {
            throw new DomainException(Vendor.CurrencyCode, currency);
        }
        var all = await taxCodes.FindAllAsync(forUpdate: false, transaction, cancellationToken);
        if (fields.TaxCodeId is { } taxCodeId)
        {
            return all.Any(t => t.Id == taxCodeId && t.UseYn) ? fields : throw new DomainException(Vendor.TaxCodeCode, taxCodeId);
        }
        var defaultCode = Vendor.DefaultTaxCode(country);
        return fields with { TaxCodeId = all.FirstOrDefault(t => t.TaxCode == defaultCode && t.UseYn)?.Id };
    }

    // BC-BR-522: the same business number on another vendor is allowed with a warning (head office and branches)
    private async Task<IReadOnlyList<ApiWarning>> WarningsAsync(
        Vendor vendor, long? exceptId, string country, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (country != CountryProfiles.Kr || vendor.Fields.BusinessRegNo is not { } regNo)
        {
            return [];
        }
        var others = await vendors.FindNamesByBusinessRegNoAsync(regNo, exceptId, transaction, cancellationToken);
        return others.Count == 0 ? [] : [new ApiWarning(Vendor.SameBusinessRegNoWarning, [string.Join(", ", others)])];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static VendorFields ToFields(VendorFieldsRequest r) => new(
        r.Contact, r.Contact2, r.Phone1, r.Phone2, r.Phone3, r.FaxNo, r.Email, r.Terms, r.ValidFlag, r.Remarks, r.Addr1, r.Addr2, r.City,
        r.State, r.Country, r.ZipCode, r.ShipAddr1, r.ShipAddr2, r.ShipCity, r.ShipState, r.ShipCountry, r.ShipZipCode, r.VendorPrice,
        r.CurrencyCode, r.TaxCodeId, r.TaxPartyType, r.BusinessRegNo, r.CeoName, r.BusinessType, r.BusinessItem);

    private static VendorDto ToDto(VendorRow r, IReadOnlyList<ApiWarning>? warnings = null)
    {
        var f = r.Fields;
        return new VendorDto(
            r.Id, r.VName, f.Contact, f.Contact2, f.Phone1, f.Phone2, f.Phone3, f.FaxNo, f.Email, f.Terms, f.ValidFlag, r.BalAmt, f.Remarks,
            f.Addr1, f.Addr2, f.City, f.State, f.Country, f.ZipCode, f.ShipAddr1, f.ShipAddr2, f.ShipCity, f.ShipState, f.ShipCountry,
            f.ShipZipCode, f.VendorPrice, f.CurrencyCode, f.TaxCodeId, r.TaxCode, f.TaxPartyType, f.BusinessRegNo, f.CeoName,
            f.BusinessType, f.BusinessItem, r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy)
        {
            Warnings = warnings ?? [],
        };
    }
}

using System.Data;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Sd.Customers;

namespace QuroErp.Application.Sd.Customers;

/// <summary>The customer master screen (SD-UC-001) and its US exemption certificate grid (BC-BR-524).</summary>
public sealed class CustomerService(
    ICustomerRepository customers, ICompanyRepository companies, ILocalizationRepository localization, ITaxCodeRepository taxCodes,
    ICodeValidator codes, IBusinessClock clock, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The customer does not exist, or a KR company asks for exemption certificates. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<CustomerListDto> SearchAsync(CustomerSearchQuery query, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(query.Size, 1, CustomerRoutes.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var (rows, total) = await customers.SearchAsync(
            new CustomerSearch(query.CName, query.ValidFlag, (page - 1) * size, size), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new CustomerListDto(rows.Select(r => ToDto(r)).ToList(), total);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<CustomerDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await customers.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <exception cref="DomainException">
    /// SD-E-301, 302, 303, 314, 331, 332, 335; BC-E-415, 521, 522, 523; Common-E-002 for an unknown code value.
    /// </exception>
    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, string createdBy, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var country = await CountryAsync(tx, cancellationToken);
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var fields = await ResolveCurrencyAndTaxCodeAsync(ToFields(request), country, tx, cancellationToken);
        var customer = Customer.Create(request.CName, fields, country, clock.Today(), Customer.BillingFlowOf(company?.BillingMode));
        if (await customers.NameExistsAsync(customer.CName, tx, cancellationToken))
        {
            throw new DomainException(Customer.DuplicateNameCode, customer.CName);
        }
        var warnings = await WarningsAsync(customer, null, country, tx, cancellationToken);
        var id = await customers.InsertAsync(customer, createdBy, tx, cancellationToken);
        var saved = await customers.FindAsync(id, forUpdate: false, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!, warnings);
    }

    /// <exception cref="DomainException">
    /// SD-E-301, 303, 314, 331, 332, 335; BC-E-415, 521, 522, 523; Common-E-002 for an unknown code value.
    /// </exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await customers.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        var country = await CountryAsync(tx, cancellationToken);
        var customer = Customer.Restore(row.Id, row.CName, row.Fields);
        customer.Change(await ResolveCurrencyAndTaxCodeAsync(ToFields(request), country, tx, cancellationToken), country);
        var warnings = await WarningsAsync(customer, id, country, tx, cancellationToken);
        if (!await customers.UpdateAsync(customer, request.UpdatedAt, updatedBy, tx, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = await customers.FindAsync(id, forUpdate: false, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!, warnings);
    }

    /// <exception cref="DomainException">SD-E-307 when the customer has documents.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await customers.FindAsync(id, forUpdate: true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != updatedAt)
        {
            throw new ConcurrencyException();
        }
        Customer.Restore(row.Id, row.CName, row.Fields).EnsureDeletable(await customers.HasDocumentsAsync(id, uow.Transaction, cancellationToken));
        if (!await customers.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>The customer's exemption certificates (BC-BR-524).</summary>
    /// <exception cref="NotFoundException">Common-E-006 for an unknown customer or a KR company.</exception>
    public async Task<IReadOnlyList<CustomerTaxExemptionDto>> GetTaxExemptionsAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await EnsureExemptionCustomerAsync(id, forUpdate: false, uow.Transaction, cancellationToken);
        var rows = await customers.FindExemptionsAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Saves the grid's added, changed and deleted certificates in one transaction and returns them all (SD-BR-333, BC-BR-524).
    /// </summary>
    /// <exception cref="DomainException">BC-E-524, SD-E-314, SD-E-333; Common-E-002 for an unknown state.</exception>
    /// <exception cref="ConcurrencyException">A row's updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006 for an unknown customer or a KR company.</exception>
    public async Task<IReadOnlyList<CustomerTaxExemptionDto>> SaveTaxExemptionsAsync(
        long id, BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey> request, string userId, CancellationToken cancellationToken)
    {
        var added = request.Added.Select(r => CustomerTaxExemptions.Clean(r.CertificateNo, r.StateCode, r.ValidTo)).ToList();
        var updated = request.Updated.Select(r => (Row: r, Certificate: CustomerTaxExemptions.Clean(r.CertificateNo, r.StateCode, r.ValidTo))).ToList();
        foreach (var state in added.Concat(updated.Select(u => u.Certificate)).Select(c => c.StateCode).Distinct())
        {
            await codes.EnsureValidAsync(CodeMajors.State, state, cancellationToken);
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        await EnsureExemptionCustomerAsync(id, forUpdate: true, tx, cancellationToken);
        var stored = (await customers.FindExemptionsAsync(id, tx, cancellationToken)).ToDictionary(r => r.Id);
        foreach (var key in request.Deleted)
        {
            if (!stored.Remove(key.Id))
            {
                throw new DomainException(CustomerTaxExemptions.NotOfCustomerCode, key.Id);
            }
            if (!await customers.DeleteExemptionAsync(key.Id, key.UpdatedAt, tx, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var (row, certificate) in updated)
        {
            if (row.Id is not { } rowId || !stored.TryGetValue(rowId, out var current))
            {
                throw new DomainException(CustomerTaxExemptions.NotOfCustomerCode, row.Id ?? 0);
            }
            if (row.UpdatedAt is not { } expected || !await customers.UpdateExemptionAsync(rowId, certificate, expected, userId, tx, cancellationToken))
            {
                throw new ConcurrencyException();
            }
            stored[rowId] = current with { Certificate = certificate };
        }
        foreach (var certificate in added)
        {
            await customers.InsertExemptionAsync(id, certificate, userId, tx, cancellationToken);
        }
        CustomerTaxExemptions.EnsureOneValidPerState(stored.Values.Select(s => s.Certificate).Concat(added), clock.Today());

        var rows = await customers.FindExemptionsAsync(id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    // Exemption certificates are a US sales tax feature: a KR company has none (BC-BR-524)
    private async Task EnsureExemptionCustomerAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (await CountryAsync(transaction, cancellationToken) == CountryProfiles.Kr
            || await customers.FindAsync(id, forUpdate, transaction, cancellationToken) is null)
        {
            throw new NotFoundException(NotFoundCode, id);
        }
    }

    // SD-BR-305
    private async Task EnsureCodesAsync(CustomerFieldsRequest r, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.SalesRep, Blank(r.Rep), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.Terms, Blank(r.Terms), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.FreightCharge, Blank(r.FreightCharge), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.DiscountStatus, Blank(r.DcStatus), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ValidFlag, Blank(r.ValidFlag), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Blank(r.State), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Blank(r.ShipState), cancellationToken);
    }

    private async Task<string> CountryAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await localization.FindCompanyAsync(transaction, cancellationToken))?.CountryCode ?? CountryProfiles.Us;

    // BC-BR-415: a currency in use, empty for the base currency. BC-BR-523: a tax code in use, empty for the country default
    private async Task<CustomerFields> ResolveCurrencyAndTaxCodeAsync(
        CustomerFields fields, string country, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var currency = string.IsNullOrWhiteSpace(fields.CurrencyCode) ? null : fields.CurrencyCode.Trim().ToUpperInvariant();
        if (currency is not null
            && !(await localization.FindCurrenciesAsync(transaction, cancellationToken)).Any(c => c.UseYn && c.CurrencyCode == currency))
        {
            throw new DomainException(Customer.CurrencyCode, currency);
        }
        var all = await taxCodes.FindAllAsync(forUpdate: false, transaction, cancellationToken);
        if (fields.TaxCodeId is { } taxCodeId)
        {
            return all.Any(t => t.Id == taxCodeId && t.UseYn) ? fields : throw new DomainException(Customer.TaxCodeCode, taxCodeId);
        }
        return Customer.DefaultTaxCode(country) is { } defaultCode
            ? fields with { TaxCodeId = all.FirstOrDefault(t => t.TaxCode == defaultCode && t.UseYn)?.Id }
            : fields;
    }

    // BC-BR-522: the same business number on another customer is allowed with a warning (head office and branches)
    private async Task<IReadOnlyList<ApiWarning>> WarningsAsync(
        Customer customer, long? exceptId, string country, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (country != CountryProfiles.Kr || customer.Fields.BusinessRegNo is not { } regNo)
        {
            return [];
        }
        var others = await customers.FindNamesByBusinessRegNoAsync(regNo, exceptId, transaction, cancellationToken);
        return others.Count == 0 ? [] : [new ApiWarning(Customer.SameBusinessRegNoWarning, [string.Join(", ", others)])];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerFields ToFields(CustomerFieldsRequest r) => new(
        r.ShipName, r.Contact, r.Contact2, r.Phone1, r.Phone2, r.Phone3, r.FaxNo, r.Email, r.Rep, r.PriceLevel, r.FreightCharge, r.Remarks,
        r.Memo2, r.Memo3, r.Terms, r.ValidFlag, r.Addr1, r.Addr2, r.City, r.State, r.Country, r.ZipCode, r.ShipAddr1, r.ShipAddr2,
        r.ShipCity, r.ShipState, r.ShipCountry, r.ShipZipCode, r.ShipPhone, r.ShipFax, r.CreditLimit, r.DcStatus, r.StartDt,
        r.BillingFlow, r.CurrencyCode, r.TaxCodeId, r.TaxPartyType, r.BusinessRegNo, r.CeoName, r.BusinessType, r.BusinessItem);

    private static CustomerTaxExemptionDto ToDto(CustomerTaxExemptionRow r) =>
        new(r.Id, r.Certificate.CertificateNo, r.Certificate.StateCode, r.Certificate.ValidTo, r.UpdatedAt);

    private static CustomerDto ToDto(CustomerRow r, IReadOnlyList<ApiWarning>? warnings = null)
    {
        var f = r.Fields;
        return new CustomerDto(
            r.Id, r.CName, f.ShipName, f.Contact, f.Contact2, f.Phone1, f.Phone2, f.Phone3, f.FaxNo, f.Email, f.Rep, f.PriceLevel,
            f.FreightCharge, r.BalAmt, f.Remarks, f.Memo2, f.Memo3, f.Terms, f.ValidFlag, f.Addr1, f.Addr2, f.City, f.State, f.Country,
            f.ZipCode, f.ShipAddr1, f.ShipAddr2, f.ShipCity, f.ShipState, f.ShipCountry, f.ShipZipCode, f.ShipPhone, f.ShipFax,
            f.CreditLimit, f.DcStatus, f.StartDt, f.BillingFlow ?? Customer.ShipAndBill, f.CurrencyCode, f.TaxCodeId, r.TaxCode,
            f.TaxPartyType, f.BusinessRegNo, f.CeoName, f.BusinessType, f.BusinessItem, r.CreatedAt, r.CreatedBy, r.UpdatedAt,
            r.UpdatedBy)
        {
            Warnings = warnings ?? [],
        };
    }
}

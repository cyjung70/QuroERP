using System.Data;
using Dapper;
using QuroErp.Application.Sd.Customers;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Sd.Customers;

namespace QuroErp.Infrastructure.Sd.Customers;

internal sealed class CustomerRepository : ICustomerRepository
{
    // SD-BR-308: the balance is the sum of the customer's invoice and Credit Memo balances; void documents are left out
    private const string Columns = """
        c.id, c.c_name, c.ship_name, c.contact, c.contact2, c.phone1, c.phone2, c.phone3, c.fax_no, c.email, c.rep, c.price_level,
        c.freight_charge, c.remarks, c.memo2, c.memo3, c.terms, c.valid_flag, c.addr1, c.addr2, c.city, c.state, c.country, c.zip_code,
        c.ship_addr1, c.ship_addr2, c.ship_city, c.ship_state, c.ship_country, c.ship_zip_code, c.ship_phone, c.ship_fax,
        c.credit_limit, c.dc_status, c.start_dt, c.billing_flow, c.currency_code, c.tax_code_id, c.tax_party_type, c.business_reg_no,
        c.ceo_name, c.business_type, c.business_item, c.created_at, c.created_by, c.updated_at, c.updated_by,
        coalesce((SELECT sum(coalesce(i.bal_amt, 0)) FROM sd_sales_invoice i
                  WHERE i.customer_id = c.id AND coalesce(i.status, '') <> 'VO'), 0) AS bal_amt,
        t.tax_code
        """;

    private const string From = " FROM bc_customer c LEFT JOIN bc_tax_code t ON t.id = c.tax_code_id";

    private const string SelectById = "SELECT " + Columns + From + " WHERE c.id = @Id";

    private const string SelectByIdForUpdate = SelectById + " FOR UPDATE OF c";

    private const string SelectNameExists = "SELECT EXISTS (SELECT 1 FROM bc_customer WHERE c_name = @CName)";

    private const string SelectNamesByBusinessRegNo = """
        SELECT c_name FROM bc_customer
        WHERE business_reg_no = @BusinessRegNo AND (@ExceptId::bigint IS NULL OR id <> @ExceptId)
        ORDER BY c_name
        """;

    private const string SearchCondition = """
        WHERE (@NamePattern::text IS NULL OR c.c_name ILIKE @NamePattern ESCAPE '\')
          AND (@ValidFlag::text IS NULL OR c.valid_flag = @ValidFlag)
        """;

    private const string Search = "SELECT " + Columns + From + " " + SearchCondition + """

        ORDER BY c.c_name
        LIMIT @Limit OFFSET @Offset
        """;

    private const string Count = "SELECT count(*) FROM bc_customer c " + SearchCondition;

    private const string Insert = """
        INSERT INTO bc_customer (c_name, ship_name, contact, contact2, phone1, phone2, phone3, fax_no, email, rep, price_level,
                                 freight_charge, remarks, memo2, memo3, terms, valid_flag, addr1, addr2, city, state, country, zip_code,
                                 ship_addr1, ship_addr2, ship_city, ship_state, ship_country, ship_zip_code, ship_phone, ship_fax,
                                 credit_limit, dc_status, start_dt, billing_flow, currency_code, tax_code_id, tax_party_type,
                                 business_reg_no, ceo_name, business_type, business_item, created_by, updated_by)
        VALUES (@CName, @ShipName, @Contact, @Contact2, @Phone1, @Phone2, @Phone3, @FaxNo, @Email, @Rep, @PriceLevel,
                @FreightCharge, @Remarks, @Memo2, @Memo3, @Terms, @ValidFlag, @Addr1, @Addr2, @City, @State, @Country, @ZipCode,
                @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipCountry, @ShipZipCode, @ShipPhone, @ShipFax,
                @CreditLimit, @DcStatus, @StartDt, @BillingFlow, @CurrencyCode, @TaxCodeId, @TaxPartyType,
                @BusinessRegNo, @CeoName, @BusinessType, @BusinessItem, @UserId, @UserId)
        RETURNING id
        """;

    // The name never changes here (SD-BR-302)
    private const string Update = """
        UPDATE bc_customer
        SET ship_name = @ShipName, contact = @Contact, contact2 = @Contact2, phone1 = @Phone1, phone2 = @Phone2, phone3 = @Phone3,
            fax_no = @FaxNo, email = @Email, rep = @Rep, price_level = @PriceLevel, freight_charge = @FreightCharge, remarks = @Remarks,
            memo2 = @Memo2, memo3 = @Memo3, terms = @Terms, valid_flag = @ValidFlag, addr1 = @Addr1, addr2 = @Addr2, city = @City,
            state = @State, country = @Country, zip_code = @ZipCode, ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2,
            ship_city = @ShipCity, ship_state = @ShipState, ship_country = @ShipCountry, ship_zip_code = @ShipZipCode,
            ship_phone = @ShipPhone, ship_fax = @ShipFax, credit_limit = @CreditLimit, dc_status = @DcStatus, start_dt = @StartDt,
            billing_flow = @BillingFlow, currency_code = @CurrencyCode, tax_code_id = @TaxCodeId, tax_party_type = @TaxPartyType,
            business_reg_no = @BusinessRegNo, ceo_name = @CeoName, business_type = @BusinessType, business_item = @BusinessItem,
            updated_at = now(), updated_by = @UserId
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    // SD-BR-307: the documents that refer to a customer
    private const string SelectHasDocuments = """
        SELECT EXISTS (SELECT 1 FROM sd_sales_order WHERE customer_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_shipment WHERE customer_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_sales_invoice WHERE customer_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_ar_receipt WHERE customer_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_customer_opening_balance WHERE customer_id = @Id)
        """;

    private const string Delete = """
        WITH gone AS (DELETE FROM bc_customer WHERE id = @Id AND updated_at = @ExpectedUpdatedAt RETURNING id)
        SELECT count(*) FROM gone
        """;

    private const string DeleteExemptionsOfCustomer = "DELETE FROM sd_customer_tax_exemption WHERE customer_id = @Id";

    private const string SelectExemptions = """
        SELECT id, certificate_no, state_code, valid_to, updated_at
        FROM sd_customer_tax_exemption
        WHERE customer_id = @CustomerId
        ORDER BY state_code, certificate_no, id
        """;

    private const string InsertExemption = """
        INSERT INTO sd_customer_tax_exemption (customer_id, certificate_no, state_code, valid_to, created_by, updated_by)
        VALUES (@CustomerId, @CertificateNo, @StateCode, @ValidTo, @UserId, @UserId)
        """;

    private const string UpdateExemption = """
        UPDATE sd_customer_tax_exemption
        SET certificate_no = @CertificateNo, state_code = @StateCode, valid_to = @ValidTo, updated_at = now(), updated_by = @UserId
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteExemption = "DELETE FROM sd_customer_tax_exemption WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    public async Task<CustomerRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? SelectByIdForUpdate : SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToCustomerRow();
    }

    public async Task<(IReadOnlyList<CustomerRow> Items, int Total)> SearchAsync(
        CustomerSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            NamePattern = string.IsNullOrWhiteSpace(search.CName) ? null : "%" + EscapeLike(search.CName.Trim()) + "%",
            ValidFlag = string.IsNullOrWhiteSpace(search.ValidFlag) ? null : search.ValidFlag.Trim(),
            search.Limit,
            search.Offset,
        };
        var rows = await transaction.Connection!.QueryAsync<Row>(
            new CommandDefinition(Search, parameters, transaction, cancellationToken: cancellationToken));
        var total = await transaction.Connection!.ExecuteScalarAsync<int>(
            new CommandDefinition(Count, parameters, transaction, cancellationToken: cancellationToken));
        return (rows.Select(r => r.ToCustomerRow()).ToList(), total);
    }

    public Task<bool> NameExistsAsync(string cName, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectNameExists, new { CName = cName }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<string>> FindNamesByBusinessRegNoAsync(
        string businessRegNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<string>(new CommandDefinition(
            SelectNamesByBusinessRegNo,
            new { BusinessRegNo = businessRegNo, ExceptId = exceptId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public Task<long> InsertAsync(Customer customer, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(
            new CommandDefinition(Insert, Parameters(customer, createdBy, null), transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        Customer customer, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, Parameters(customer, updatedBy, expectedUpdatedAt), transaction, cancellationToken: cancellationToken)) == 1;

    public Task<bool> HasDocumentsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectHasDocuments, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(DeleteExemptionsOfCustomer, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return await transaction.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            Delete,
            // Npgsql only accepts UTC values for timestamptz parameters
            new { Id = id, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<CustomerTaxExemptionRow>> FindExemptionsAsync(
        long customerId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ExemptionRow>(new CommandDefinition(
            SelectExemptions, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new CustomerTaxExemptionRow(
            r.Id, new TaxExemptionCertificate(r.CertificateNo, r.StateCode, r.ValidTo), Utc(r.UpdatedAt)))
        .ToList();

    public Task InsertExemptionAsync(
        long customerId, TaxExemptionCertificate certificate, string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertExemption,
            new { CustomerId = customerId, certificate.CertificateNo, certificate.StateCode, certificate.ValidTo, UserId = userId },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateExemptionAsync(
        long id, TaxExemptionCertificate certificate, DateTimeOffset expectedUpdatedAt, string userId, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateExemption,
            new
            {
                Id = id,
                certificate.CertificateNo,
                certificate.StateCode,
                certificate.ValidTo,
                UserId = userId,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteExemptionAsync(
        long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteExemption, new { Id = id, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime }, transaction,
            cancellationToken: cancellationToken)) == 1;

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DynamicParameters Parameters(Customer customer, string userId, DateTimeOffset? expectedUpdatedAt)
    {
        var f = customer.Fields;
        var parameters = new DynamicParameters(new
        {
            customer.Id,
            customer.CName,
            f.ShipName,
            f.Contact,
            f.Contact2,
            f.Phone1,
            f.Phone2,
            f.Phone3,
            f.FaxNo,
            f.Email,
            f.Rep,
            f.PriceLevel,
            f.FreightCharge,
            f.Remarks,
            f.Memo2,
            f.Memo3,
            f.Terms,
            f.ValidFlag,
            f.Addr1,
            f.Addr2,
            f.City,
            f.State,
            f.Country,
            f.ZipCode,
            f.ShipAddr1,
            f.ShipAddr2,
            f.ShipCity,
            f.ShipState,
            f.ShipCountry,
            f.ShipZipCode,
            f.ShipPhone,
            f.ShipFax,
            f.CreditLimit,
            f.DcStatus,
            f.StartDt,
            f.BillingFlow,
            f.CurrencyCode,
            f.TaxCodeId,
            f.TaxPartyType,
            f.BusinessRegNo,
            f.CeoName,
            f.BusinessType,
            f.BusinessItem,
            UserId = userId,
        });
        if (expectedUpdatedAt is { } expected)
        {
            parameters.Add("ExpectedUpdatedAt", expected.UtcDateTime);
        }
        return parameters;
    }

    private sealed record ExemptionRow(long Id, string CertificateNo, string StateCode, DateOnly? ValidTo, DateTime UpdatedAt);

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id,
        string CName,
        string? ShipName,
        string? Contact,
        string? Contact2,
        string? Phone1,
        string? Phone2,
        string? Phone3,
        string? FaxNo,
        string? Email,
        string? Rep,
        string? PriceLevel,
        string? FreightCharge,
        string? Remarks,
        string? Memo2,
        string? Memo3,
        string? Terms,
        string? ValidFlag,
        string? Addr1,
        string? Addr2,
        string? City,
        string? State,
        string? Country,
        string? ZipCode,
        string? ShipAddr1,
        string? ShipAddr2,
        string? ShipCity,
        string? ShipState,
        string? ShipCountry,
        string? ShipZipCode,
        string? ShipPhone,
        string? ShipFax,
        decimal? CreditLimit,
        string? DcStatus,
        DateOnly? StartDt,
        string BillingFlow,
        string? CurrencyCode,
        long? TaxCodeId,
        string? TaxPartyType,
        string? BusinessRegNo,
        string? CeoName,
        string? BusinessType,
        string? BusinessItem,
        DateTime CreatedAt,
        string CreatedBy,
        DateTime UpdatedAt,
        string UpdatedBy,
        decimal BalAmt,
        string? TaxCode)
    {
        public CustomerRow ToCustomerRow() => new(
            Id,
            CName,
            new CustomerFields(
                ShipName, Contact, Contact2, Phone1, Phone2, Phone3, FaxNo, Email, Rep, PriceLevel, FreightCharge, Remarks, Memo2, Memo3,
                Terms, ValidFlag, Addr1, Addr2, City, State, Country, ZipCode, ShipAddr1, ShipAddr2, ShipCity, ShipState, ShipCountry,
                ShipZipCode, ShipPhone, ShipFax, CreditLimit, DcStatus, StartDt, BillingFlow, CurrencyCode, TaxCodeId, TaxPartyType,
                BusinessRegNo, CeoName, BusinessType, BusinessItem),
            BalAmt,
            TaxCode,
            Utc(CreatedAt),
            CreatedBy,
            Utc(UpdatedAt),
            UpdatedBy);
    }
}

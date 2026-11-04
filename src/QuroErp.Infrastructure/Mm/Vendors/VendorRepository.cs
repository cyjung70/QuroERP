using System.Data;
using Dapper;
using QuroErp.Application.Mm.Vendors;
using QuroErp.Domain.Mm.Vendors;

namespace QuroErp.Infrastructure.Mm.Vendors;

internal sealed class VendorRepository : IVendorRepository
{
    // MM-BR-858: the unpaid balance. Goods receipts no longer carry one (TASK-MM-007, MM-BR-112); it is 0 until supplier
    // invoices hold the balance (MM-BR-755)
    private const string Columns = """
        v.id, v.v_name, v.contact, v.contact2, v.phone1, v.phone2, v.phone3, v.fax_no, v.email, v.terms, v.valid_flag, v.remarks,
        v.addr1, v.addr2, v.city, v.state, v.country, v.zip_code, v.ship_addr1, v.ship_addr2, v.ship_city, v.ship_state,
        v.ship_country, v.ship_zip_code, v.vendor_price, v.currency_code, v.tax_code_id, v.tax_party_type, v.business_reg_no,
        v.ceo_name, v.business_type, v.business_item, v.created_at, v.created_by, v.updated_at, v.updated_by,
        0::numeric AS bal_amt,
        t.tax_code
        """;

    private const string From = " FROM bc_vendor v LEFT JOIN bc_tax_code t ON t.id = v.tax_code_id";

    private const string SelectById = "SELECT " + Columns + From + " WHERE v.id = @Id";

    private const string SelectByIdForUpdate = SelectById + " FOR UPDATE OF v";

    private const string SelectNameExists = "SELECT EXISTS (SELECT 1 FROM bc_vendor WHERE v_name = @VName)";

    private const string SelectNamesByBusinessRegNo = """
        SELECT v_name FROM bc_vendor
        WHERE business_reg_no = @BusinessRegNo AND (@ExceptId::bigint IS NULL OR id <> @ExceptId)
        ORDER BY v_name
        """;

    private const string SelectNext = "SELECT " + Columns + From + """

        WHERE v.v_name > (SELECT v_name FROM bc_vendor WHERE id = @Id)
        ORDER BY v.v_name
        LIMIT 1
        """;

    private const string SelectPrevious = "SELECT " + Columns + From + """

        WHERE v.v_name < (SELECT v_name FROM bc_vendor WHERE id = @Id)
        ORDER BY v.v_name DESC
        LIMIT 1
        """;

    private const string SearchCondition = """
        WHERE (@NamePattern::text IS NULL OR v.v_name ILIKE @NamePattern ESCAPE '\')
          AND (@ValidFlag::text IS NULL OR v.valid_flag = @ValidFlag)
        """;

    private const string Search = "SELECT " + Columns + From + " " + SearchCondition + """

        ORDER BY v.v_name
        LIMIT @Limit OFFSET @Offset
        """;

    private const string Count = "SELECT count(*) FROM bc_vendor v " + SearchCondition;

    private const string Insert = """
        INSERT INTO bc_vendor (v_name, contact, contact2, phone1, phone2, phone3, fax_no, email, terms, valid_flag, remarks, addr1, addr2,
                               city, state, country, zip_code, ship_addr1, ship_addr2, ship_city, ship_state, ship_country,
                               ship_zip_code, vendor_price, currency_code, tax_code_id, tax_party_type, business_reg_no, ceo_name,
                               business_type, business_item, created_by, updated_by)
        VALUES (@VName, @Contact, @Contact2, @Phone1, @Phone2, @Phone3, @FaxNo, @Email, @Terms, @ValidFlag, @Remarks, @Addr1, @Addr2,
                @City, @State, @Country, @ZipCode, @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipCountry,
                @ShipZipCode, @VendorPrice, @CurrencyCode, @TaxCodeId, @TaxPartyType, @BusinessRegNo, @CeoName,
                @BusinessType, @BusinessItem, @UserId, @UserId)
        RETURNING id
        """;

    // The name never changes (MM-BR-852)
    private const string Update = """
        UPDATE bc_vendor
        SET contact = @Contact, contact2 = @Contact2, phone1 = @Phone1, phone2 = @Phone2, phone3 = @Phone3, fax_no = @FaxNo,
            email = @Email, terms = @Terms, valid_flag = @ValidFlag, remarks = @Remarks, addr1 = @Addr1, addr2 = @Addr2, city = @City,
            state = @State, country = @Country, zip_code = @ZipCode, ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2,
            ship_city = @ShipCity, ship_state = @ShipState, ship_country = @ShipCountry, ship_zip_code = @ShipZipCode,
            vendor_price = @VendorPrice, currency_code = @CurrencyCode, tax_code_id = @TaxCodeId, tax_party_type = @TaxPartyType,
            business_reg_no = @BusinessRegNo, ceo_name = @CeoName, business_type = @BusinessType, business_item = @BusinessItem,
            updated_at = now(), updated_by = @UserId
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM bc_vendor WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    // MM-BR-857. Purchase prices are kept per price group: they block only when no other vendor uses the group
    private const string SelectInUse = """
        SELECT EXISTS (SELECT 1 FROM mm_purchase_order WHERE vendor_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_goods_receipt WHERE vendor_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_ap_payment WHERE vendor_id = @Id)
            OR EXISTS (SELECT 1
                       FROM bc_vendor v
                       JOIN bc_purchase_price p ON p.vendor_price = v.vendor_price
                       WHERE v.id = @Id
                         AND NOT EXISTS (SELECT 1 FROM bc_vendor o WHERE o.vendor_price = v.vendor_price AND o.id <> v.id))
        """;

    public async Task<VendorRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? SelectByIdForUpdate : SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToVendorRow();
    }

    public async Task<VendorRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forward ? SelectNext : SelectPrevious, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToVendorRow();
    }

    public async Task<(IReadOnlyList<VendorRow> Items, int Total)> SearchAsync(
        VendorSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            NamePattern = string.IsNullOrWhiteSpace(search.VName) ? null : "%" + EscapeLike(search.VName.Trim()) + "%",
            ValidFlag = string.IsNullOrWhiteSpace(search.ValidFlag) ? null : search.ValidFlag.Trim(),
            search.Limit,
            search.Offset,
        };
        var rows = await transaction.Connection!.QueryAsync<Row>(
            new CommandDefinition(Search, parameters, transaction, cancellationToken: cancellationToken));
        var total = await transaction.Connection!.ExecuteScalarAsync<int>(
            new CommandDefinition(Count, parameters, transaction, cancellationToken: cancellationToken));
        return (rows.Select(r => r.ToVendorRow()).ToList(), total);
    }

    public Task<bool> NameExistsAsync(string vName, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectNameExists, new { VName = vName }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<string>> FindNamesByBusinessRegNoAsync(
        string businessRegNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<string>(new CommandDefinition(
            SelectNamesByBusinessRegNo,
            new { BusinessRegNo = businessRegNo, ExceptId = exceptId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public Task<long> InsertAsync(Vendor vendor, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(
            new CommandDefinition(Insert, Parameters(vendor, createdBy, null), transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        Vendor vendor, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, Parameters(vendor, updatedBy, expectedUpdatedAt), transaction, cancellationToken: cancellationToken)) == 1;

    public Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectInUse, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete,
            // Npgsql only accepts UTC values for timestamptz parameters
            new { Id = id, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static DynamicParameters Parameters(Vendor vendor, string userId, DateTimeOffset? expectedUpdatedAt)
    {
        var f = vendor.Fields;
        var parameters = new DynamicParameters(new
        {
            vendor.Id,
            vendor.VName,
            f.Contact,
            f.Contact2,
            f.Phone1,
            f.Phone2,
            f.Phone3,
            f.FaxNo,
            f.Email,
            f.Terms,
            f.ValidFlag,
            f.Remarks,
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
            f.VendorPrice,
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

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id,
        string VName,
        string? Contact,
        string? Contact2,
        string? Phone1,
        string? Phone2,
        string? Phone3,
        string? FaxNo,
        string? Email,
        string? Terms,
        string? ValidFlag,
        string? Remarks,
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
        string? VendorPrice,
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
        public VendorRow ToVendorRow() => new(
            Id,
            VName,
            new VendorFields(
                Contact, Contact2, Phone1, Phone2, Phone3, FaxNo, Email, Terms, ValidFlag, Remarks, Addr1, Addr2, City, State, Country,
                ZipCode, ShipAddr1, ShipAddr2, ShipCity, ShipState, ShipCountry, ShipZipCode, VendorPrice, CurrencyCode, TaxCodeId,
                TaxPartyType, BusinessRegNo, CeoName, BusinessType, BusinessItem),
            BalAmt,
            TaxCode,
            Utc(CreatedAt),
            CreatedBy,
            Utc(UpdatedAt),
            UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

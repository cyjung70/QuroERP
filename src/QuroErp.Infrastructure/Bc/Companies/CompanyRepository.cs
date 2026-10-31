using System.Data;
using Dapper;
using QuroErp.Application.Bc.Companies;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Domain.Bc.Companies;

namespace QuroErp.Infrastructure.Bc.Companies;

internal sealed class CompanyRepository : ICompanyRepository
{
    // Single company (BC-BR-301): the lowest id is the company
    private const string Select = """
        SELECT name, billto, shipto, contact, tel_no, fax_no, register_no, represent, email, business_type, business_item,
               country_code, base_currency_code, default_lang_code, time_zone, billing_mode, logo_image IS NOT NULL AS has_logo, updated_at
        FROM bc_company
        WHERE id = (SELECT min(id) FROM bc_company)
        """;

    private const string Lock = Select + " FOR UPDATE";

    private const string Update = """
        UPDATE bc_company
        SET name = @Name, billto = @Billto, shipto = @Shipto, contact = @Contact, tel_no = @TelNo, fax_no = @FaxNo,
            register_no = @RegisterNo, represent = @Represent, email = @Email, business_type = @BusinessType, business_item = @BusinessItem,
            country_code = @CountryCode, base_currency_code = @BaseCurrencyCode, default_lang_code = @DefaultLangCode,
            time_zone = @TimeZone, billing_mode = @BillingMode, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = (SELECT min(id) FROM bc_company) AND updated_at = @ExpectedUpdatedAt
        """;

    // The same tables as QuroERP.Database CompanySetup, which blocks the installation country change (BC-BR-312, BC-BR-313)
    private const string SelectHasTransactions = """
        SELECT EXISTS (SELECT 1 FROM mm_purchase_order)
            OR EXISTS (SELECT 1 FROM mm_goods_movement)
            OR EXISTS (SELECT 1 FROM mm_ap_payment)
            OR EXISTS (SELECT 1 FROM sd_sales_order)
            OR EXISTS (SELECT 1 FROM sd_sales_invoice)
            OR EXISTS (SELECT 1 FROM sd_ar_receipt)
            OR EXISTS (SELECT 1 FROM bc_exchange_rate)
        """;

    private const string SelectHasUnbilledShipments = """
        SELECT EXISTS (
            SELECT 1
            FROM sd_shipment_line l
            JOIN sd_shipment s ON s.id = l.shipment_id
            WHERE s.status <> @VoidStatus AND l.ship_qty > l.billed_qty)
        """;

    private const string SelectLogo = """
        SELECT logo_image, logo_content_type
        FROM bc_company
        WHERE logo_image IS NOT NULL
          AND id = (SELECT min(id) FROM bc_company)
        """;

    private const string UpdateLogo = """
        UPDATE bc_company
        SET logo_image = @Image, logo_content_type = @ContentType, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = (SELECT min(id) FROM bc_company)
        """;

    private const string VoidShipmentStatus = "VO";

    public async Task<CompanyDto?> FindAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<CompanyRow>(
            new CommandDefinition(forUpdate ? Lock : Select, transaction: transaction, cancellationToken: cancellationToken));
        return row?.ToDto();
    }

    public async Task<bool> UpdateAsync(
        CompanyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                fields.Name,
                fields.Billto,
                fields.Shipto,
                fields.Contact,
                fields.TelNo,
                fields.FaxNo,
                fields.RegisterNo,
                fields.Represent,
                fields.Email,
                fields.BusinessType,
                fields.BusinessItem,
                fields.CountryCode,
                fields.BaseCurrencyCode,
                fields.DefaultLangCode,
                fields.TimeZone,
                fields.BillingMode,
                UpdatedBy = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task<bool> HasTransactionsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectHasTransactions, transaction: transaction, cancellationToken: cancellationToken));

    public Task<bool> HasUnbilledShipmentsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            SelectHasUnbilledShipments, new { VoidStatus = VoidShipmentStatus }, transaction, cancellationToken: cancellationToken));

    public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<CompanyLogoRow>(
            new CommandDefinition(SelectLogo, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateLogoAsync(
        byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var affected = await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLogo, new { Image = image, ContentType = contentType, UpdatedBy = updatedBy }, transaction, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteLogoAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var affected = await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLogo, new { Image = (byte[]?)null, ContentType = (string?)null, UpdatedBy = updatedBy }, transaction, cancellationToken: cancellationToken));
        return affected > 0;
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record CompanyRow(
        string Name, string? Billto, string? Shipto, string? Contact, string? TelNo, string? FaxNo, string? RegisterNo, string? Represent,
        string? Email, string? BusinessType, string? BusinessItem, string CountryCode, string BaseCurrencyCode, string DefaultLangCode,
        string TimeZone, string BillingMode, bool HasLogo, DateTime UpdatedAt)
    {
        public CompanyDto ToDto() => new(
            Name, Billto, Shipto, Contact, TelNo, FaxNo, RegisterNo, Represent, Email, BusinessType, BusinessItem, CountryCode, BaseCurrencyCode,
            DefaultLangCode, TimeZone, BillingMode, HasLogo, new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    }
}

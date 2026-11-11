using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Sd.Customers;

/// <summary>Customer master API routes (SD-UC-001).</summary>
public static class CustomerRoutes
{
    /// <summary>GET list (query <see cref="CustomerSearchQuery"/>), POST new customer.</summary>
    public const string Base = "api/sd/customers";

    /// <summary>GET and PUT {Base}/{id}/tax-exemptions: the US exemption certificates (BC-BR-524). 404 in a KR company.</summary>
    public const string TaxExemptions = "tax-exemptions";

    /// <summary>Default and largest page size (api.md "기타").</summary>
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
}

/// <summary>
/// One customer. BalAmt is read-only: the sum of the balances of the customer's invoices and Credit Memos (SD-BR-308). TaxCode is
/// the code of TaxCodeId. Warnings are filled only by a save, e.g. BC-W-522 for a business number another customer also has.
/// </summary>
public sealed record CustomerDto(
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
    decimal BalAmt,
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
    string? TaxCode,
    string? TaxPartyType,
    string? BusinessRegNo,
    string? CeoName,
    string? BusinessType,
    string? BusinessItem,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy) : AuditInfo(CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
{
    public IReadOnlyList<ApiWarning> Warnings { get; init; } = [];
}

/// <summary>A page of customers in name order.</summary>
public sealed record CustomerListDto(IReadOnlyList<CustomerDto> Items, int Total);

/// <summary>List conditions. All are optional.</summary>
public sealed class CustomerSearchQuery
{
    /// <summary>Name contains this (case-insensitive).</summary>
    public string? CName { get; init; }

    public string? ValidFlag { get; init; }

    /// <summary>1-based.</summary>
    public int Page { get; init; } = 1;

    public int Size { get; init; } = CustomerRoutes.DefaultPageSize;
}

/// <summary>
/// Values a user enters. Code values are Minor codes: Rep SALES_REP, Terms TERMS, FreightCharge FREIGHT_CHARGE, DcStatus
/// DISCOUNT_STATUS, ValidFlag VALID_FLAG, State and ShipState STATE (SD-BR-305). An empty ship address 1 copies the billing
/// address to the ship address (SD-BR-303); an empty PriceLevel becomes the name (SD-BR-304). BillingFlow is SHIP_AND_BILL,
/// SHIP_THEN_BILL or BILL_THEN_SHIP (SD-BR-335). CurrencyCode is a currency in use, empty for the base currency (BC-BR-415).
/// TaxCodeId is required in a US company (the ship-to tax code, SD-BR-331); empty gets VAT10 in KR and NONE elsewhere
/// (BC-BR-523). In a KR company TaxPartyType is BUSINESS or INDIVIDUAL, BUSINESS needs BusinessRegNo and CeoName, zip codes have
/// 5 digits and states stay empty (SD-BR-332, BC-BR-521, BC-BR-522).
/// </summary>
public abstract record CustomerFieldsRequest
{
    public string? ShipName { get; init; }
    public string? Contact { get; init; }
    public string? Contact2 { get; init; }
    public string? Phone1 { get; init; }
    public string? Phone2 { get; init; }
    public string? Phone3 { get; init; }
    public string? FaxNo { get; init; }
    public string? Email { get; init; }
    public string? Rep { get; init; }
    public string? PriceLevel { get; init; }
    public string? FreightCharge { get; init; }
    public string? Remarks { get; init; }
    public string? Memo2 { get; init; }
    public string? Memo3 { get; init; }
    public string? Terms { get; init; }
    public string? ValidFlag { get; init; }
    public string? Addr1 { get; init; }
    public string? Addr2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? ZipCode { get; init; }
    public string? ShipAddr1 { get; init; }
    public string? ShipAddr2 { get; init; }
    public string? ShipCity { get; init; }
    public string? ShipState { get; init; }
    public string? ShipCountry { get; init; }
    public string? ShipZipCode { get; init; }
    public string? ShipPhone { get; init; }
    public string? ShipFax { get; init; }
    public decimal? CreditLimit { get; init; }
    public string? DcStatus { get; init; }
    public DateOnly? StartDt { get; init; }
    public string? BillingFlow { get; init; }
    public string? CurrencyCode { get; init; }
    public long? TaxCodeId { get; init; }
    public string? TaxPartyType { get; init; }
    public string? BusinessRegNo { get; init; }
    public string? CeoName { get; init; }
    public string? BusinessType { get; init; }
    public string? BusinessItem { get; init; }
}

/// <summary>
/// New customer. Missing Terms, ValidFlag, Country, ShipCountry, StartDt, CreditLimit and BillingFlow get COD_CHECK, Y, USA (not
/// in KR), USA, today, 0 and the company billing default (SD-BR-301, SD-BR-335).
/// </summary>
public sealed record CreateCustomerRequest : CustomerFieldsRequest
{
    /// <summary>Stored trimmed in upper case, up to 100 characters, unique (SD-BR-302).</summary>
    public string? CName { get; init; }
}

/// <summary>Changed customer. The name cannot change here (SD-BR-302).</summary>
public sealed record UpdateCustomerRequest : CustomerFieldsRequest
{
    /// <summary>UpdatedAt of the customer as read; 409 Common-E-004 when it changed since.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One exemption certificate of a US customer (BC-BR-524). ValidTo null means no expiry.</summary>
public sealed record CustomerTaxExemptionDto(long Id, string CertificateNo, string StateCode, DateOnly? ValidTo, DateTimeOffset UpdatedAt);

/// <summary>An added or changed certificate row. Id and UpdatedAt are empty on an added row.</summary>
public sealed record CustomerTaxExemptionSaveRow(long? Id, string? CertificateNo, string? StateCode, DateOnly? ValidTo, DateTimeOffset? UpdatedAt);

/// <summary>A deleted certificate: its Id and the updatedAt it read.</summary>
public sealed record CustomerTaxExemptionKey(long Id, DateTimeOffset UpdatedAt);

using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Mm.Vendors;

/// <summary>Vendor master API routes (MM-UC-001).</summary>
public static class VendorRoutes
{
    /// <summary>GET list (query <see cref="VendorSearchQuery"/>), POST new vendor.</summary>
    public const string Base = "api/mm/vendors";

    /// <summary>GET {Base}/{id}/previous and {Base}/{id}/next: the vendor before or after it in name order. 204 when there is none.</summary>
    public const string Previous = "previous";
    public const string Next = "next";

    /// <summary>Default and largest page size (api.md "기타").</summary>
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
}

/// <summary>
/// One vendor. BalAmt is read-only: the sum of the balances of the vendor's goods receipts (MM-BR-858). TaxCode is the code of
/// TaxCodeId. Warnings are filled only by a save, e.g. BC-W-522 for a business number another vendor also has.
/// </summary>
public sealed record VendorDto(
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
    decimal BalAmt,
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

/// <summary>A page of vendors in name order (the screen's previous and next order).</summary>
public sealed record VendorListDto(IReadOnlyList<VendorDto> Items, int Total);

/// <summary>List conditions. All are optional.</summary>
public sealed class VendorSearchQuery
{
    /// <summary>Name contains this (case-insensitive).</summary>
    public string? VName { get; init; }

    public string? ValidFlag { get; init; }

    /// <summary>1-based.</summary>
    public int Page { get; init; } = 1;

    public int Size { get; init; } = VendorRoutes.DefaultPageSize;
}

/// <summary>
/// Values a user enters. Code values are Minor codes: Terms TERMS, ValidFlag VALID_FLAG, State and ShipState STATE.
/// An empty ship address 1 copies the billing address to the ship address (MM-BR-853); an empty VendorPrice becomes the name
/// (MM-BR-854). CurrencyCode is a currency in use, empty for the base currency (BC-BR-415). An empty TaxCodeId gets VAT10 in KR
/// and NONE elsewhere (BC-BR-523). In a KR company TaxPartyType is BUSINESS or INDIVIDUAL; BUSINESS needs BusinessRegNo and
/// CeoName (MM-BR-872, BC-BR-521, BC-BR-522).
/// </summary>
public abstract record VendorFieldsRequest
{
    public string? Contact { get; init; }
    public string? Contact2 { get; init; }
    public string? Phone1 { get; init; }
    public string? Phone2 { get; init; }
    public string? Phone3 { get; init; }
    public string? FaxNo { get; init; }
    public string? Email { get; init; }
    public string? Terms { get; init; }
    public string? ValidFlag { get; init; }
    public string? Remarks { get; init; }
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
    public string? VendorPrice { get; init; }
    public string? CurrencyCode { get; init; }
    public long? TaxCodeId { get; init; }
    public string? TaxPartyType { get; init; }
    public string? BusinessRegNo { get; init; }
    public string? CeoName { get; init; }
    public string? BusinessType { get; init; }
    public string? BusinessItem { get; init; }
}

/// <summary>New vendor. Missing Terms, ValidFlag, Country and ShipCountry get NET_30, Y, USA and USA (MM-BR-851).</summary>
public sealed record CreateVendorRequest : VendorFieldsRequest
{
    /// <summary>Stored trimmed in upper case, up to 100 characters, unique (MM-BR-852).</summary>
    public string? VName { get; init; }
}

/// <summary>Changed vendor. The name cannot change (MM-BR-852).</summary>
public sealed record UpdateVendorRequest : VendorFieldsRequest
{
    /// <summary>UpdatedAt of the vendor as read; 409 Common-E-004 when it changed since.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

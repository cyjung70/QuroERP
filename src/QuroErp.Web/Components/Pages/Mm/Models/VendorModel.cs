using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Vendors;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// Editable vendor on the vendor master screen (MM-UC-001). <see cref="VendorDto"/> is immutable, so the form binds to this.
/// The tax code is bound by its code; the page turns it into the id the API takes.
/// </summary>
public sealed class VendorModel
{
    // New vendor defaults shown on the screen (MM-BR-851); the API applies the same defaults
    public const string DefaultTerms = "NET_30";
    public const string DefaultValidFlag = "Y";
    public const string DefaultCountry = "USA";

    // KR tax party types (MM-BR-872)
    public const string Business = "BUSINESS";
    public const string Individual = "INDIVIDUAL";

    public long Id { get; set; }
    public string? VName { get; set; }
    public string? Contact { get; set; }
    public string? Contact2 { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Phone3 { get; set; }
    public string? FaxNo { get; set; }
    public string? Email { get; set; }
    public string? Terms { get; set; } = DefaultTerms;
    public string? ValidFlag { get; set; } = DefaultValidFlag;

    /// <summary>Read only: the sum of the vendor's goods receipt balances (MM-BR-858).</summary>
    public decimal? BalAmt { get; set; } = 0m;

    public string? Remarks { get; set; }
    public string? Addr1 { get; set; }
    public string? Addr2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; } = DefaultCountry;
    public string? ZipCode { get; set; }
    public string? ShipAddr1 { get; set; }
    public string? ShipAddr2 { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipCountry { get; set; } = DefaultCountry;
    public string? ShipZipCode { get; set; }
    public string? VendorPrice { get; set; }

    /// <summary>Empty for the base currency (BC-BR-415).</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Tax code; empty gets the country default (BC-BR-523).</summary>
    public string? TaxCode { get; set; }

    public string? TaxPartyType { get; set; }
    public string? BusinessRegNo { get; set; }
    public string? CeoName { get; set; }
    public string? BusinessType { get; set; }
    public string? BusinessItem { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who created and last changed the record, for the audit area only.</summary>
    public AuditInfo? Audit { get; set; }

    public static VendorModel From(VendorDto dto) => new()
    {
        Id = dto.Id,
        VName = dto.VName,
        Contact = dto.Contact,
        Contact2 = dto.Contact2,
        Phone1 = dto.Phone1,
        Phone2 = dto.Phone2,
        Phone3 = dto.Phone3,
        FaxNo = dto.FaxNo,
        Email = dto.Email,
        Terms = dto.Terms,
        ValidFlag = dto.ValidFlag,
        BalAmt = dto.BalAmt,
        Remarks = dto.Remarks,
        Addr1 = dto.Addr1,
        Addr2 = dto.Addr2,
        City = dto.City,
        State = dto.State,
        Country = dto.Country,
        ZipCode = dto.ZipCode,
        ShipAddr1 = dto.ShipAddr1,
        ShipAddr2 = dto.ShipAddr2,
        ShipCity = dto.ShipCity,
        ShipState = dto.ShipState,
        ShipCountry = dto.ShipCountry,
        ShipZipCode = dto.ShipZipCode,
        VendorPrice = dto.VendorPrice,
        CurrencyCode = dto.CurrencyCode,
        TaxCode = dto.TaxCode,
        TaxPartyType = dto.TaxPartyType,
        BusinessRegNo = dto.BusinessRegNo,
        CeoName = dto.CeoName,
        BusinessType = dto.BusinessType,
        BusinessItem = dto.BusinessItem,
        UpdatedAt = dto.UpdatedAt,
        Audit = dto,
    };

    /// <param name="taxCodeId">The id of <see cref="TaxCode"/>; null when it is empty.</param>
    public CreateVendorRequest ToCreateRequest(long? taxCodeId) => Fill(new CreateVendorRequest { VName = VName }, taxCodeId);

    /// <param name="taxCodeId">The id of <see cref="TaxCode"/>; null when it is empty.</param>
    public UpdateVendorRequest ToUpdateRequest(long? taxCodeId) => Fill(new UpdateVendorRequest { UpdatedAt = UpdatedAt }, taxCodeId);

    // with on the base record clones the derived one, so its own properties stay
    private T Fill<T>(T request, long? taxCodeId) where T : VendorFieldsRequest => (T)((VendorFieldsRequest)request with
    {
        Contact = Contact,
        Contact2 = Contact2,
        Phone1 = Phone1,
        Phone2 = Phone2,
        Phone3 = Phone3,
        FaxNo = FaxNo,
        Email = Email,
        Terms = Terms,
        ValidFlag = ValidFlag,
        Remarks = Remarks,
        Addr1 = Addr1,
        Addr2 = Addr2,
        City = City,
        State = State,
        Country = Country,
        ZipCode = ZipCode,
        ShipAddr1 = ShipAddr1,
        ShipAddr2 = ShipAddr2,
        ShipCity = ShipCity,
        ShipState = ShipState,
        ShipCountry = ShipCountry,
        ShipZipCode = ShipZipCode,
        VendorPrice = VendorPrice,
        CurrencyCode = CurrencyCode,
        TaxCodeId = taxCodeId,
        TaxPartyType = TaxPartyType,
        BusinessRegNo = BusinessRegNo,
        CeoName = CeoName,
        BusinessType = BusinessType,
        BusinessItem = BusinessItem,
    });
}

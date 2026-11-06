using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>
/// Editable customer on the customer master screen (SD-UC-001). <see cref="CustomerDto"/> is immutable, so the form binds to
/// this. The tax code is bound by its code; the page turns it into the id the API takes.
/// </summary>
public sealed class CustomerModel
{
    // New customer defaults shown on the screen (SD-BR-301); the API applies the same defaults. The billing flow default comes
    // from the company setting, so a new customer leaves it empty for the API to fill (SD-BR-335)
    public const string DefaultTerms = "COD_CHECK";
    public const string DefaultValidFlag = "Y";
    public const string DefaultCountry = "USA";

    // Billing flows (SD-BR-335)
    public const string ShipAndBill = "SHIP_AND_BILL";
    public const string ShipThenBill = "SHIP_THEN_BILL";
    public const string BillThenShip = "BILL_THEN_SHIP";
    public static readonly string[] BillingFlows = [ShipAndBill, ShipThenBill, BillThenShip];

    // KR tax party types (SD-BR-332)
    public const string Business = "BUSINESS";
    public const string Individual = "INDIVIDUAL";

    public long Id { get; set; }
    public string? CName { get; set; }
    public string? ShipName { get; set; }
    public string? Contact { get; set; }
    public string? Contact2 { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Phone3 { get; set; }
    public string? FaxNo { get; set; }
    public string? Email { get; set; }
    public string? Rep { get; set; }
    public string? PriceLevel { get; set; }
    public string? FreightCharge { get; set; }

    /// <summary>Read only: the sum of the customer's invoice and Credit Memo balances (SD-BR-308).</summary>
    public decimal? BalAmt { get; set; } = 0m;

    public string? Remarks { get; set; }
    public string? Memo2 { get; set; }
    public string? Memo3 { get; set; }
    public string? Terms { get; set; } = DefaultTerms;
    public string? ValidFlag { get; set; } = DefaultValidFlag;
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
    public string? ShipPhone { get; set; }
    public string? ShipFax { get; set; }
    public decimal? CreditLimit { get; set; } = 0m;
    public string? DcStatus { get; set; }
    /// <summary>Empty on a new customer: the API takes the company business date (SD-BR-301).</summary>
    public DateOnly? StartDt { get; set; }

    /// <summary>Empty on a new customer: the API takes the company default (SD-BR-335).</summary>
    public string? BillingFlow { get; set; }

    /// <summary>Empty for the base currency (BC-BR-415).</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Tax code; required in a US company (SD-BR-331), empty gets the country default elsewhere (BC-BR-523).</summary>
    public string? TaxCode { get; set; }

    public string? TaxPartyType { get; set; }
    public string? BusinessRegNo { get; set; }
    public string? CeoName { get; set; }
    public string? BusinessType { get; set; }
    public string? BusinessItem { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who created and last changed the record, for the audit area only.</summary>
    public AuditInfo? Audit { get; set; }

    public static CustomerModel From(CustomerDto dto) => new()
    {
        Id = dto.Id,
        CName = dto.CName,
        ShipName = dto.ShipName,
        Contact = dto.Contact,
        Contact2 = dto.Contact2,
        Phone1 = dto.Phone1,
        Phone2 = dto.Phone2,
        Phone3 = dto.Phone3,
        FaxNo = dto.FaxNo,
        Email = dto.Email,
        Rep = dto.Rep,
        PriceLevel = dto.PriceLevel,
        FreightCharge = dto.FreightCharge,
        BalAmt = dto.BalAmt,
        Remarks = dto.Remarks,
        Memo2 = dto.Memo2,
        Memo3 = dto.Memo3,
        Terms = dto.Terms,
        ValidFlag = dto.ValidFlag,
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
        ShipPhone = dto.ShipPhone,
        ShipFax = dto.ShipFax,
        CreditLimit = dto.CreditLimit,
        DcStatus = dto.DcStatus,
        StartDt = dto.StartDt,
        BillingFlow = dto.BillingFlow,
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
    public CreateCustomerRequest ToCreateRequest(long? taxCodeId) => Fill(new CreateCustomerRequest { CName = CName }, taxCodeId);

    /// <param name="taxCodeId">The id of <see cref="TaxCode"/>; null when it is empty.</param>
    public UpdateCustomerRequest ToUpdateRequest(long? taxCodeId) => Fill(new UpdateCustomerRequest { UpdatedAt = UpdatedAt }, taxCodeId);

    // with on the base record clones the derived one, so its own properties stay
    private T Fill<T>(T request, long? taxCodeId) where T : CustomerFieldsRequest => (T)((CustomerFieldsRequest)request with
    {
        ShipName = ShipName,
        Contact = Contact,
        Contact2 = Contact2,
        Phone1 = Phone1,
        Phone2 = Phone2,
        Phone3 = Phone3,
        FaxNo = FaxNo,
        Email = Email,
        Rep = Rep,
        PriceLevel = PriceLevel,
        FreightCharge = FreightCharge,
        Remarks = Remarks,
        Memo2 = Memo2,
        Memo3 = Memo3,
        Terms = Terms,
        ValidFlag = ValidFlag,
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
        ShipPhone = ShipPhone,
        ShipFax = ShipFax,
        CreditLimit = CreditLimit,
        DcStatus = DcStatus,
        StartDt = StartDt,
        BillingFlow = BillingFlow,
        CurrencyCode = CurrencyCode,
        TaxCodeId = taxCodeId,
        TaxPartyType = TaxPartyType,
        BusinessRegNo = BusinessRegNo,
        CeoName = CeoName,
        BusinessType = BusinessType,
        BusinessItem = BusinessItem,
    });
}

using System.Text.RegularExpressions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Sd.Customers;

/// <summary>
/// Values the customer API may change (SD-UC-001). The name is not here: it is set once and changed only by the customer name
/// change (SD-BR-302). The balance is not here either: it is the sum of the customer's invoice balances (SD-BR-308).
/// </summary>
public sealed record CustomerFields(
    string? ShipName = null,
    string? Contact = null,
    string? Contact2 = null,
    string? Phone1 = null,
    string? Phone2 = null,
    string? Phone3 = null,
    string? FaxNo = null,
    string? Email = null,
    string? Rep = null,
    string? PriceLevel = null,
    string? FreightCharge = null,
    string? Remarks = null,
    string? Memo2 = null,
    string? Memo3 = null,
    string? Terms = null,
    string? ValidFlag = null,
    string? Addr1 = null,
    string? Addr2 = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? ZipCode = null,
    string? ShipAddr1 = null,
    string? ShipAddr2 = null,
    string? ShipCity = null,
    string? ShipState = null,
    string? ShipCountry = null,
    string? ShipZipCode = null,
    string? ShipPhone = null,
    string? ShipFax = null,
    decimal? CreditLimit = null,
    string? DcStatus = null,
    DateOnly? StartDt = null,
    string? BillingFlow = null,
    string? CurrencyCode = null,
    long? TaxCodeId = null,
    string? TaxPartyType = null,
    string? BusinessRegNo = null,
    string? CeoName = null,
    string? BusinessType = null,
    string? BusinessItem = null);

/// <summary>Customer master (bc_customer) and its rules SD-BR-301~307, 314, 331, 332, 335 with BC-BR-521~523.</summary>
public sealed partial class Customer
{
    /// <summary>Required value missing. Arg: property name.</summary>
    public const string RequiredCode = "SD-E-301";

    /// <summary>Name already used by another customer. Arg: name.</summary>
    public const string DuplicateNameCode = "SD-E-302";

    /// <summary>Ship address given without a ship zip code.</summary>
    public const string ShipZipCodeRequiredCode = "SD-E-303";

    /// <summary>Delete of a customer with documents; set it not in use instead. Arg: name.</summary>
    public const string InUseCode = "SD-E-307";

    /// <summary>Text too long or e-mail not in the right form. Args: property name, maximum length.</summary>
    public const string InvalidTextCode = "SD-E-314";

    /// <summary>US customer without a ship-to tax code. Arg: property name.</summary>
    public const string UsTaxCodeRequiredCode = "SD-E-331";

    /// <summary>KR customer value not allowed: party type, a zip code that is not 5 digits, or a state. Arg: property name.</summary>
    public const string KrFieldCode = "SD-E-332";

    /// <summary>Billing flow that is not one of the three values. Arg: the value.</summary>
    public const string BillingFlowCode = "SD-E-335";

    /// <summary>KR business customer without its business number or CEO name (BC-BR-522). Arg: property name.</summary>
    public const string KrBusinessRequiredCode = "BC-E-522";

    /// <summary>Currency that is not a currency in use (BC-BR-415). Arg: currency code.</summary>
    public const string CurrencyCode = "BC-E-415";

    /// <summary>Tax code that does not exist or is not in use (BC-BR-523). Arg: tax code id.</summary>
    public const string TaxCodeCode = "BC-E-523";

    /// <summary>Another customer has the same KR business number; the save goes on (BC-BR-522). Arg: the other customer names.</summary>
    public const string SameBusinessRegNoWarning = "BC-W-522";

    public const string DefaultTerms = "COD_CHECK";
    public const string ValidFlagYes = "Y";
    public const string DefaultCountry = "USA";

    public const string PartyBusiness = "BUSINESS";
    public const string PartyIndividual = "INDIVIDUAL";

    /// <summary>Billing flows (BC-BR-316, SD-BR-335).</summary>
    public const string ShipAndBill = "SHIP_AND_BILL";
    public const string ShipThenBill = "SHIP_THEN_BILL";
    public const string BillThenShip = "BILL_THEN_SHIP";

    public static IReadOnlyList<string> BillingFlows { get; } = [ShipAndBill, ShipThenBill, BillThenShip];

    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 100;
    public const int MemoMaxLength = 200;

    // SD-BR-314; the KR fields follow the vendor lengths
    private static readonly (string Name, Func<CustomerFields, string?> Value, int Max)[] TextLimits =
    [
        (nameof(CustomerFields.ShipName), f => f.ShipName, NameMaxLength),
        (nameof(CustomerFields.Contact), f => f.Contact, 100),
        (nameof(CustomerFields.Contact2), f => f.Contact2, 100),
        (nameof(CustomerFields.Phone1), f => f.Phone1, 30),
        (nameof(CustomerFields.Phone2), f => f.Phone2, 30),
        (nameof(CustomerFields.Phone3), f => f.Phone3, 30),
        (nameof(CustomerFields.FaxNo), f => f.FaxNo, 30),
        (nameof(CustomerFields.Email), f => f.Email, EmailMaxLength),
        (nameof(CustomerFields.PriceLevel), f => f.PriceLevel, NameMaxLength),
        (nameof(CustomerFields.Remarks), f => f.Remarks, MemoMaxLength),
        (nameof(CustomerFields.Memo2), f => f.Memo2, MemoMaxLength),
        (nameof(CustomerFields.Memo3), f => f.Memo3, MemoMaxLength),
        (nameof(CustomerFields.Addr1), f => f.Addr1, 100),
        (nameof(CustomerFields.Addr2), f => f.Addr2, 100),
        (nameof(CustomerFields.City), f => f.City, 40),
        (nameof(CustomerFields.Country), f => f.Country, 20),
        (nameof(CustomerFields.ZipCode), f => f.ZipCode, 20),
        (nameof(CustomerFields.ShipAddr1), f => f.ShipAddr1, 100),
        (nameof(CustomerFields.ShipAddr2), f => f.ShipAddr2, 100),
        (nameof(CustomerFields.ShipCity), f => f.ShipCity, 40),
        (nameof(CustomerFields.ShipCountry), f => f.ShipCountry, 20),
        (nameof(CustomerFields.ShipZipCode), f => f.ShipZipCode, 20),
        (nameof(CustomerFields.ShipPhone), f => f.ShipPhone, 30),
        (nameof(CustomerFields.ShipFax), f => f.ShipFax, 30),
        (nameof(CustomerFields.BusinessRegNo), f => f.BusinessRegNo, 30),
        (nameof(CustomerFields.CeoName), f => f.CeoName, 30),
        (nameof(CustomerFields.BusinessType), f => f.BusinessType, 50),
        (nameof(CustomerFields.BusinessItem), f => f.BusinessItem, 50),
    ];

    private Customer(long id, string cName, CustomerFields fields)
    {
        Id = id;
        CName = cName;
        Fields = fields;
    }

    public long Id { get; }

    /// <summary>Trimmed, upper case and unique; this API never changes it (SD-BR-302).</summary>
    public string CName { get; }

    /// <summary>Checked values: blank text is null, the ship address and price level are filled.</summary>
    public CustomerFields Fields { get; private set; }

    /// <summary>
    /// A new customer. Terms, use flag, country, ship country, start date, credit limit and billing flow get their defaults when
    /// missing (SD-BR-301, SD-BR-335). Name uniqueness, code values, the currency and the tax code are checked by the caller.
    /// </summary>
    /// <param name="countryCode">The company country: KR checks the KR fields (SD-BR-332), US the ship-to tax code (SD-BR-331).</param>
    /// <param name="today">The business date, the default start date.</param>
    /// <param name="defaultBillingFlow">The company's billing flow default (BC-BR-316).</param>
    /// <exception cref="DomainException">SD-E-301, 303, 314, 331, 332, 335; BC-E-521, 522.</exception>
    public static Customer Create(string? cName, CustomerFields fields, string countryCode, DateOnly today, string defaultBillingFlow)
    {
        var name = NormalizeName(cName) ?? throw new DomainException(RequiredCode, nameof(CName));
        if (name.Length > NameMaxLength)
        {
            throw new DomainException(InvalidTextCode, nameof(CName), NameMaxLength);
        }
        var cleaned = Clean(fields);
        cleaned = cleaned with
        {
            Terms = cleaned.Terms ?? DefaultTerms,
            ValidFlag = cleaned.ValidFlag ?? ValidFlagYes,
            Country = cleaned.Country ?? (countryCode == CountryProfiles.Kr ? null : DefaultCountry),
            ShipCountry = cleaned.ShipCountry ?? (countryCode == CountryProfiles.Kr ? null : DefaultCountry),
            StartDt = cleaned.StartDt ?? today,
            CreditLimit = cleaned.CreditLimit ?? 0m,
            BillingFlow = cleaned.BillingFlow ?? defaultBillingFlow,
        };
        return new Customer(0, name, Check(name, cleaned, countryCode));
    }

    /// <summary>A customer read from the database; its values are not checked again.</summary>
    public static Customer Restore(long id, string cName, CustomerFields fields) => new(id, cName, fields);

    /// <summary>Replaces the editable values; the name stays (SD-BR-302). No defaults are filled on a change.</summary>
    /// <exception cref="DomainException">SD-E-301, 303, 314, 331, 332, 335; BC-E-521, 522.</exception>
    public void Change(CustomerFields fields, string countryCode) => Fields = Check(CName, Clean(fields), countryCode);

    /// <summary>A customer with sales orders, shipments, invoices, Credit Memos or receipts cannot be deleted (SD-BR-307).</summary>
    /// <exception cref="DomainException">SD-E-307.</exception>
    public void EnsureDeletable(bool hasDocuments)
    {
        if (hasDocuments)
        {
            throw new DomainException(InUseCode, CName);
        }
    }

    /// <summary>Trimmed upper case name, or null when blank (SD-BR-302). Also used to find a customer by name.</summary>
    public static string? NormalizeName(string? cName) => string.IsNullOrWhiteSpace(cName) ? null : cName.Trim().ToUpperInvariant();

    /// <summary>
    /// The tax code of a customer saved without one (BC-BR-523): VAT10 in KR, NONE outside KR and US. A US customer has no default:
    /// its ship-to tax code is required (SD-BR-331).
    /// </summary>
    public static string? DefaultTaxCode(string countryCode) => countryCode switch
    {
        CountryProfiles.Kr => "VAT10",
        CountryProfiles.Us => null,
        _ => TaxCodeRules.NoneTaxCode,
    };

    /// <summary>
    /// The company billing setting as a billing flow (BC-BR-316): WITH_SHIPMENT is SHIP_AND_BILL and SEPARATE is SHIP_THEN_BILL.
    /// A billing flow value is kept as it is; anything else is SHIP_AND_BILL.
    /// </summary>
    public static string BillingFlowOf(string? companyBilling) => companyBilling switch
    {
        "SEPARATE" => ShipThenBill,
        ShipThenBill or BillThenShip => companyBilling,
        _ => ShipAndBill,
    };

    private static CustomerFields Clean(CustomerFields f) => f with
    {
        ShipName = Text(f.ShipName)?.ToUpperInvariant(),
        Contact = Text(f.Contact),
        Contact2 = Text(f.Contact2),
        Phone1 = Text(f.Phone1),
        Phone2 = Text(f.Phone2),
        Phone3 = Text(f.Phone3),
        FaxNo = Text(f.FaxNo),
        Email = Text(f.Email),
        Rep = Text(f.Rep),
        PriceLevel = Text(f.PriceLevel),
        FreightCharge = Text(f.FreightCharge),
        Remarks = Text(f.Remarks),
        Memo2 = Text(f.Memo2),
        Memo3 = Text(f.Memo3),
        Terms = Text(f.Terms),
        ValidFlag = Text(f.ValidFlag),
        Addr1 = Text(f.Addr1),
        Addr2 = Text(f.Addr2),
        City = Text(f.City),
        State = Text(f.State),
        Country = Text(f.Country),
        ZipCode = Text(f.ZipCode),
        ShipAddr1 = Text(f.ShipAddr1),
        ShipAddr2 = Text(f.ShipAddr2),
        ShipCity = Text(f.ShipCity),
        ShipState = Text(f.ShipState),
        ShipCountry = Text(f.ShipCountry),
        ShipZipCode = Text(f.ShipZipCode),
        ShipPhone = Text(f.ShipPhone),
        ShipFax = Text(f.ShipFax),
        DcStatus = Text(f.DcStatus),
        BillingFlow = Text(f.BillingFlow)?.ToUpperInvariant(),
        CurrencyCode = Text(f.CurrencyCode)?.ToUpperInvariant(),
        TaxPartyType = Text(f.TaxPartyType)?.ToUpperInvariant(),
        BusinessRegNo = Text(f.BusinessRegNo),
        CeoName = Text(f.CeoName),
        BusinessType = Text(f.BusinessType),
        BusinessItem = Text(f.BusinessItem),
    };

    private static CustomerFields Check(string name, CustomerFields f, string countryCode)
    {
        // SD-BR-301
        foreach (var (field, value) in new[]
        {
            (nameof(CustomerFields.ShipName), f.ShipName), (nameof(CustomerFields.Phone1), f.Phone1),
            (nameof(CustomerFields.Rep), f.Rep), (nameof(CustomerFields.Terms), f.Terms), (nameof(CustomerFields.ZipCode), f.ZipCode),
        })
        {
            if (value is null)
            {
                throw new DomainException(RequiredCode, field);
            }
        }

        // SD-BR-303: no ship address means the billing address is the ship address
        f = f.ShipAddr1 is null
            ? f with
            {
                ShipAddr1 = f.Addr1,
                ShipAddr2 = f.Addr2,
                ShipCity = f.City,
                ShipState = f.State,
                ShipCountry = f.Country,
                ShipZipCode = f.ZipCode,
            }
            : f;
        if (f.ShipZipCode is null)
        {
            throw new DomainException(ShipZipCodeRequiredCode);
        }

        // SD-BR-304: each customer has its own price list by default
        f = f with { PriceLevel = f.PriceLevel ?? name };

        // SD-BR-335
        if (f.BillingFlow is null || !BillingFlows.Contains(f.BillingFlow))
        {
            throw new DomainException(BillingFlowCode, f.BillingFlow ?? "");
        }

        if (countryCode == CountryProfiles.Kr)
        {
            f = CheckKr(f);
        }
        else if (countryCode == CountryProfiles.Us && f.TaxCodeId is null)
        {
            // SD-BR-331, BC-BR-523: the US default is the ship-to tax area code, which the user picks
            throw new DomainException(UsTaxCodeRequiredCode, nameof(CustomerFields.TaxCodeId));
        }

        // SD-BR-314
        foreach (var (field, value, max) in TextLimits)
        {
            if (value(f)?.Length > max)
            {
                throw new DomainException(InvalidTextCode, field, max);
            }
        }
        if (f.Email is not null && !EmailPattern().IsMatch(f.Email))
        {
            throw new DomainException(InvalidTextCode, nameof(CustomerFields.Email), EmailMaxLength);
        }
        return f;
    }

    // SD-BR-332, BC-BR-521, BC-BR-522
    private static CustomerFields CheckKr(CustomerFields f)
    {
        if (f.TaxPartyType is not (PartyBusiness or PartyIndividual))
        {
            throw new DomainException(KrFieldCode, nameof(CustomerFields.TaxPartyType));
        }
        if (f.TaxPartyType == PartyBusiness)
        {
            foreach (var (field, value) in new[]
            {
                (nameof(CustomerFields.BusinessRegNo), f.BusinessRegNo), (nameof(CustomerFields.CeoName), f.CeoName),
            })
            {
                if (value is null)
                {
                    throw new DomainException(KrBusinessRequiredCode, field);
                }
            }
        }
        foreach (var (field, value) in new[]
        {
            (nameof(CustomerFields.ZipCode), f.ZipCode), (nameof(CustomerFields.ShipZipCode), f.ShipZipCode),
        })
        {
            if (value is not null && !KrZipCode().IsMatch(value))
            {
                throw new DomainException(KrFieldCode, field);
            }
        }
        foreach (var (field, value) in new[] { (nameof(CustomerFields.State), f.State), (nameof(CustomerFields.ShipState), f.ShipState) })
        {
            if (value is not null)
            {
                throw new DomainException(KrFieldCode, field);
            }
        }
        return f.BusinessRegNo is null ? f : f with { BusinessRegNo = BusinessRegistrationNo.Normalize(f.BusinessRegNo) };
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex KrZipCode();
}

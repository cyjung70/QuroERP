using System.Text.RegularExpressions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Vendors;

/// <summary>
/// Values the vendor API may change (MM-UC-001). The name is not here: it is set once (MM-BR-852). The unpaid balance is not
/// here either: it is the sum of the vendor's goods receipt balances (MM-BR-858).
/// </summary>
public sealed record VendorFields(
    string? Contact = null,
    string? Contact2 = null,
    string? Phone1 = null,
    string? Phone2 = null,
    string? Phone3 = null,
    string? FaxNo = null,
    string? Email = null,
    string? Terms = null,
    string? ValidFlag = null,
    string? Remarks = null,
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
    string? VendorPrice = null,
    string? CurrencyCode = null,
    long? TaxCodeId = null,
    string? TaxPartyType = null,
    string? BusinessRegNo = null,
    string? CeoName = null,
    string? BusinessType = null,
    string? BusinessItem = null);

/// <summary>Vendor master (bc_vendor) and its rules MM-BR-851~859, 871, 872 with BC-BR-521~523.</summary>
public sealed partial class Vendor
{
    /// <summary>Required value missing. Arg: property name.</summary>
    public const string RequiredCode = "MM-E-851";

    /// <summary>Name already used by another vendor. Arg: name.</summary>
    public const string DuplicateNameCode = "MM-E-852";

    /// <summary>Ship address given without a ship zip code.</summary>
    public const string ShipZipCodeRequiredCode = "MM-E-853";

    /// <summary>Remarks too long. Arg: maximum length.</summary>
    public const string RemarksTooLongCode = "MM-E-856";

    /// <summary>Delete of a vendor used by a document or a purchase price. Arg: name.</summary>
    public const string InUseCode = "MM-E-857";

    /// <summary>Text too long or e-mail not in the right form. Args: property name, maximum length.</summary>
    public const string InvalidTextCode = "MM-E-859";

    /// <summary>KR vendor without a valid party type. Arg: property name.</summary>
    public const string KrPartyTypeCode = "MM-E-872";

    /// <summary>KR business vendor without its business number or CEO name (BC-BR-522). Arg: property name.</summary>
    public const string KrBusinessRequiredCode = "BC-E-522";

    /// <summary>Currency that is not a currency in use (BC-BR-415). Arg: currency code.</summary>
    public const string CurrencyCode = "BC-E-415";

    /// <summary>Tax code that does not exist or is not in use (BC-BR-523). Arg: tax code id.</summary>
    public const string TaxCodeCode = "BC-E-523";

    /// <summary>Another vendor has the same KR business number; the save goes on (BC-BR-522). Arg: the other vendor names.</summary>
    public const string SameBusinessRegNoWarning = "BC-W-522";

    public const string DefaultTerms = "NET_30";
    public const string ValidFlagYes = "Y";
    public const string DefaultCountry = "USA";

    public const string PartyBusiness = "BUSINESS";
    public const string PartyIndividual = "INDIVIDUAL";

    public const int RemarksMaxLength = 200;

    // MM-BR-859; the KR fields follow the business place lengths
    private static readonly (string Name, Func<VendorFields, string?> Value, int Max)[] TextLimits =
    [
        (nameof(VendorFields.Contact), f => f.Contact, 100),
        (nameof(VendorFields.Contact2), f => f.Contact2, 100),
        (nameof(VendorFields.Phone1), f => f.Phone1, 30),
        (nameof(VendorFields.Phone2), f => f.Phone2, 30),
        (nameof(VendorFields.Phone3), f => f.Phone3, 30),
        (nameof(VendorFields.FaxNo), f => f.FaxNo, 30),
        (nameof(VendorFields.Email), f => f.Email, EmailMaxLength),
        (nameof(VendorFields.Addr1), f => f.Addr1, 100),
        (nameof(VendorFields.Addr2), f => f.Addr2, 100),
        (nameof(VendorFields.City), f => f.City, 40),
        (nameof(VendorFields.Country), f => f.Country, 20),
        (nameof(VendorFields.ZipCode), f => f.ZipCode, 20),
        (nameof(VendorFields.ShipAddr1), f => f.ShipAddr1, 100),
        (nameof(VendorFields.ShipAddr2), f => f.ShipAddr2, 100),
        (nameof(VendorFields.ShipCity), f => f.ShipCity, 40),
        (nameof(VendorFields.ShipCountry), f => f.ShipCountry, 20),
        (nameof(VendorFields.ShipZipCode), f => f.ShipZipCode, 20),
        (nameof(VendorFields.VendorPrice), f => f.VendorPrice, NameMaxLength),
        (nameof(VendorFields.BusinessRegNo), f => f.BusinessRegNo, 30),
        (nameof(VendorFields.CeoName), f => f.CeoName, 30),
        (nameof(VendorFields.BusinessType), f => f.BusinessType, 50),
        (nameof(VendorFields.BusinessItem), f => f.BusinessItem, 50),
    ];

    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 100;

    private Vendor(long id, string vName, VendorFields fields)
    {
        Id = id;
        VName = vName;
        Fields = fields;
    }

    public long Id { get; }

    /// <summary>Trimmed, upper case and unique; never changes after the first save (MM-BR-852).</summary>
    public string VName { get; }

    /// <summary>Checked values: blank text is null, the ship address and price group are filled.</summary>
    public VendorFields Fields { get; private set; }

    /// <summary>
    /// A new vendor. Terms, use flag, country and ship country get their defaults when missing (MM-BR-851).
    /// Name uniqueness and code values are checked by the caller.
    /// </summary>
    /// <param name="krCompany">The company country is KR: the KR tax fields are checked (MM-BR-872).</param>
    /// <exception cref="DomainException">MM-E-851, 853, 856, 859, 872; BC-E-521, 522.</exception>
    public static Vendor Create(string? vName, VendorFields fields, bool krCompany)
    {
        var name = NormalizeName(vName);
        if (name is null)
        {
            throw new DomainException(RequiredCode, nameof(VName));
        }
        if (name.Length > NameMaxLength)
        {
            throw new DomainException(InvalidTextCode, nameof(VName), NameMaxLength);
        }
        var cleaned = Clean(fields);
        cleaned = cleaned with
        {
            Terms = cleaned.Terms ?? DefaultTerms,
            ValidFlag = cleaned.ValidFlag ?? ValidFlagYes,
            Country = cleaned.Country ?? DefaultCountry,
            ShipCountry = cleaned.ShipCountry ?? DefaultCountry,
        };
        return new Vendor(0, name, Check(name, cleaned, krCompany));
    }

    /// <summary>A vendor read from the database; its values are not checked again.</summary>
    public static Vendor Restore(long id, string vName, VendorFields fields) => new(id, vName, fields);

    /// <summary>Replaces the editable values; the name stays (MM-BR-852). No defaults are filled on a change.</summary>
    /// <exception cref="DomainException">MM-E-851, 853, 856, 859, 872; BC-E-521, 522.</exception>
    public void Change(VendorFields fields, bool krCompany) => Fields = Check(VName, Clean(fields), krCompany);

    /// <summary>A vendor used by a purchase order, goods receipt, payment or purchase price cannot be deleted (MM-BR-857).</summary>
    /// <exception cref="DomainException">MM-E-857.</exception>
    public void EnsureDeletable(bool inUse)
    {
        if (inUse)
        {
            throw new DomainException(InUseCode, VName);
        }
    }

    /// <summary>Trimmed upper case name, or null when blank (MM-BR-852). Also used to find a vendor by name.</summary>
    public static string? NormalizeName(string? vName) => string.IsNullOrWhiteSpace(vName) ? null : vName.Trim().ToUpperInvariant();

    /// <summary>The tax code of a vendor saved without one: VAT10 in KR, NONE elsewhere (BC-BR-523).</summary>
    public static string DefaultTaxCode(string countryCode) =>
        countryCode == CountryProfiles.Kr ? "VAT10" : TaxCodeRules.NoneTaxCode;

    private static VendorFields Clean(VendorFields f) => f with
    {
        Contact = Text(f.Contact),
        Contact2 = Text(f.Contact2),
        Phone1 = Text(f.Phone1),
        Phone2 = Text(f.Phone2),
        Phone3 = Text(f.Phone3),
        FaxNo = Text(f.FaxNo),
        Email = Text(f.Email),
        Terms = Text(f.Terms),
        ValidFlag = Text(f.ValidFlag),
        Remarks = Text(f.Remarks),
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
        VendorPrice = Text(f.VendorPrice),
        CurrencyCode = Text(f.CurrencyCode)?.ToUpperInvariant(),
        TaxPartyType = Text(f.TaxPartyType)?.ToUpperInvariant(),
        BusinessRegNo = Text(f.BusinessRegNo),
        CeoName = Text(f.CeoName),
        BusinessType = Text(f.BusinessType),
        BusinessItem = Text(f.BusinessItem),
    };

    private static VendorFields Check(string name, VendorFields f, bool krCompany)
    {
        // MM-BR-851
        foreach (var (field, value) in new[]
        {
            (nameof(VendorFields.Phone1), f.Phone1), (nameof(VendorFields.ZipCode), f.ZipCode),
        })
        {
            if (value is null)
            {
                throw new DomainException(RequiredCode, field);
            }
        }

        // MM-BR-853: no ship address means the billing address is the ship address
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

        // MM-BR-854
        f = f with { VendorPrice = f.VendorPrice ?? name };

        // MM-BR-856
        if (f.Remarks?.Length > RemarksMaxLength)
        {
            throw new DomainException(RemarksTooLongCode, RemarksMaxLength);
        }

        if (krCompany)
        {
            f = CheckKr(f);
        }

        // MM-BR-859
        foreach (var (field, value, max) in TextLimits)
        {
            if (value(f)?.Length > max)
            {
                throw new DomainException(InvalidTextCode, field, max);
            }
        }
        if (f.Email is not null && !EmailPattern().IsMatch(f.Email))
        {
            throw new DomainException(InvalidTextCode, nameof(VendorFields.Email), EmailMaxLength);
        }
        return f;
    }

    // MM-BR-872, BC-BR-521, BC-BR-522
    private static VendorFields CheckKr(VendorFields f)
    {
        if (f.TaxPartyType is not (PartyBusiness or PartyIndividual))
        {
            throw new DomainException(KrPartyTypeCode, nameof(VendorFields.TaxPartyType));
        }
        if (f.TaxPartyType == PartyBusiness)
        {
            foreach (var (field, value) in new[]
            {
                (nameof(VendorFields.BusinessRegNo), f.BusinessRegNo), (nameof(VendorFields.CeoName), f.CeoName),
            })
            {
                if (value is null)
                {
                    throw new DomainException(KrBusinessRequiredCode, field);
                }
            }
        }
        return f.BusinessRegNo is null ? f : f with { BusinessRegNo = BusinessRegistrationNo.Normalize(f.BusinessRegNo) };
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

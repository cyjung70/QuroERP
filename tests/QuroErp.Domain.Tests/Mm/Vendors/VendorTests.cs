using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Vendors;

namespace QuroErp.Domain.Tests.Mm.Vendors;

public class VendorTests
{
    private static readonly VendorFields Valid = new(Phone1: "555-0100", ZipCode: "90001", Addr1: "1 Main St", City: "Los Angeles", State: "CA");

    private static readonly VendorFields KrBusiness = Valid with
    {
        TaxPartyType = "business",
        BusinessRegNo = "220-81-62517",
        CeoName = "Kim",
        BusinessType = "Wholesale",
        BusinessItem = "Parts",
    };

    private static DomainException Error(Action act) => act.Should().Throw<DomainException>().Which;

    private static Vendor Stored() => Vendor.Restore(5, "ACME", Valid with { Terms = "NET_30", ValidFlag = "Y", ShipZipCode = "90001" });

    [Fact, Trait("Id", "MM-UT-851")]
    public void Create_FillsDefaults_AndBlankTextIsNull()
    {
        var vendor = Vendor.Create("Acme", Valid with { Contact = "  " }, krCompany: false);

        vendor.Fields.Terms.Should().Be("NET_30");
        vendor.Fields.ValidFlag.Should().Be("Y");
        vendor.Fields.Country.Should().Be("USA");
        vendor.Fields.Contact.Should().BeNull();
    }

    [Theory, Trait("Id", "MM-UT-851")]
    [InlineData(null, "555", "1", "VName")]
    [InlineData(" ", "555", "1", "VName")]
    [InlineData("ACME", null, "1", "Phone1")]
    [InlineData("ACME", "555", " ", "ZipCode")]
    public void Create_RequiredValueMissing_RejectsMmE851(string? name, string? phone1, string? zipCode, string field)
    {
        var error = Error(() => Vendor.Create(name, Valid with { Phone1 = phone1, ZipCode = zipCode }, krCompany: false));

        error.Code.Should().Be(Vendor.RequiredCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "MM-UT-851")]
    public void Change_DoesNotFillDefaults_ButStillNeedsTheRequiredValues()
    {
        var vendor = Stored();

        vendor.Change(Valid, krCompany: false);

        vendor.Fields.Terms.Should().BeNull();
        Error(() => vendor.Change(Valid with { Phone1 = "" }, krCompany: false)).Code.Should().Be(Vendor.RequiredCode);
    }

    [Fact, Trait("Id", "MM-UT-852")]
    public void Create_TrimsTheName_AndStoresItInUpperCase()
    {
        Vendor.Create("  Acme Supply ", Valid, krCompany: false).VName.Should().Be("ACME SUPPLY");
        Vendor.NormalizeName(" acme ").Should().Be("ACME");
    }

    [Fact, Trait("Id", "MM-UT-852")]
    public void Change_KeepsTheName()
    {
        var vendor = Stored();

        vendor.Change(Valid with { Contact = "Lee" }, krCompany: false);

        vendor.VName.Should().Be("ACME");
        vendor.Fields.Contact.Should().Be("Lee");
    }

    [Fact, Trait("Id", "MM-UT-853")]
    public void Create_WithoutShipAddress_CopiesTheBillingAddress()
    {
        var vendor = Vendor.Create("ACME", Valid with { Addr2 = "Suite 2", Country = "US" }, krCompany: false);

        var f = vendor.Fields;
        (f.ShipAddr1, f.ShipAddr2, f.ShipCity, f.ShipState, f.ShipCountry, f.ShipZipCode)
            .Should().Be(("1 Main St", "Suite 2", "Los Angeles", "CA", "US", "90001"));
    }

    [Fact, Trait("Id", "MM-UT-853")]
    public void Create_WithShipAddress_KeepsIt_AndNeedsTheShipZipCode()
    {
        var withZip = Vendor.Create("ACME", Valid with { ShipAddr1 = "9 Dock Rd", ShipZipCode = "10001" }, krCompany: false);

        withZip.Fields.ShipAddr1.Should().Be("9 Dock Rd");
        withZip.Fields.ShipCity.Should().BeNull();
        withZip.Fields.ShipZipCode.Should().Be("10001");
        Error(() => Vendor.Create("ACME", Valid with { ShipAddr1 = "9 Dock Rd" }, krCompany: false))
            .Code.Should().Be(Vendor.ShipZipCodeRequiredCode);
    }

    [Fact, Trait("Id", "MM-UT-854")]
    public void Create_WithoutPriceGroup_UsesTheName()
    {
        Vendor.Create("acme", Valid, krCompany: false).Fields.VendorPrice.Should().Be("ACME");
        Vendor.Create("acme", Valid with { VendorPrice = "GROUP1" }, krCompany: false).Fields.VendorPrice.Should().Be("GROUP1");
    }

    [Fact, Trait("Id", "MM-UT-856")]
    public void Create_KeepsRemarks_UpTo200Characters()
    {
        Vendor.Create("ACME", Valid with { Remarks = new string('r', 200) }, krCompany: false).Fields.Remarks.Should().HaveLength(200);

        var error = Error(() => Vendor.Create("ACME", Valid with { Remarks = new string('r', 201) }, krCompany: false));

        error.Code.Should().Be(Vendor.RemarksTooLongCode);
        error.Args.Should().Equal(200);
    }

    [Fact, Trait("Id", "MM-UT-857")]
    public void EnsureDeletable_AVendorInUse_RejectsMmE857()
    {
        Stored().Invoking(v => v.EnsureDeletable(inUse: false)).Should().NotThrow();

        var error = Error(() => Stored().EnsureDeletable(inUse: true));

        error.Code.Should().Be(Vendor.InUseCode);
        error.Args.Should().Equal("ACME");
    }

    [Theory, Trait("Id", "MM-UT-859")]
    [InlineData("Contact", 100)]
    [InlineData("Phone1", 30)]
    [InlineData("FaxNo", 30)]
    [InlineData("Addr1", 100)]
    [InlineData("ShipAddr1", 100)]
    [InlineData("City", 40)]
    [InlineData("Country", 20)]
    [InlineData("ZipCode", 20)]
    public void Create_TextLongerThanTheLimit_RejectsMmE859(string field, int max)
    {
        var fields = field switch
        {
            "Contact" => Valid with { Contact = new string('c', max + 1) },
            "Phone1" => Valid with { Phone1 = new string('1', max + 1) },
            "FaxNo" => Valid with { FaxNo = new string('1', max + 1) },
            "Addr1" => Valid with { Addr1 = new string('a', max + 1) },
            "ShipAddr1" => Valid with { ShipAddr1 = new string('a', max + 1), ShipZipCode = "1" },
            "City" => Valid with { City = new string('c', max + 1) },
            "Country" => Valid with { Country = new string('c', max + 1) },
            _ => Valid with { ZipCode = new string('9', max + 1) },
        };

        var error = Error(() => Vendor.Create("ACME", fields, krCompany: false));

        error.Code.Should().Be(Vendor.InvalidTextCode);
        error.Args.Should().Equal(field, max);
    }

    [Fact, Trait("Id", "MM-UT-859")]
    public void Create_NameOf100Characters_IsKept_AndLongerIsRejected()
    {
        Vendor.Create(new string('A', 100), Valid, krCompany: false).VName.Should().HaveLength(100);

        Error(() => Vendor.Create(new string('A', 101), Valid, krCompany: false)).Args.Should().Equal("VName", 100);
    }

    [Theory, Trait("Id", "MM-UT-859")]
    [InlineData("buyer@acme.com", true)]
    [InlineData("buyer@acme", false)]
    [InlineData("buyer acme.com", false)]
    public void Create_EmailIsCheckedWhenEntered(string email, bool valid)
    {
        Action act = () => Vendor.Create("ACME", Valid with { Email = email }, krCompany: false);

        if (valid)
        {
            act.Should().NotThrow();
        }
        else
        {
            Error(act).Args.Should().Equal("Email", 100);
        }
    }

    [Fact, Trait("Id", "MM-UT-872")]
    public void Create_KrBusinessVendor_NormalizesThePartyTypeAndTheBusinessNumber()
    {
        var vendor = Vendor.Create("ACME", KrBusiness, krCompany: true);

        vendor.Fields.TaxPartyType.Should().Be("BUSINESS");
        vendor.Fields.BusinessRegNo.Should().Be("2208162517");
    }

    [Theory, Trait("Id", "MM-UT-872")]
    [InlineData(null)]
    [InlineData("COMPANY")]
    public void Create_KrVendorWithoutAValidPartyType_RejectsMmE872(string? partyType)
    {
        var error = Error(() => Vendor.Create("ACME", KrBusiness with { TaxPartyType = partyType }, krCompany: true));

        error.Code.Should().Be(Vendor.KrPartyTypeCode);
        error.Args.Should().Equal("TaxPartyType");
    }

    [Theory, Trait("Id", "MM-UT-872"), Trait("Rule", "BC-BR-522")]
    [InlineData("BusinessRegNo")]
    [InlineData("CeoName")]
    public void Create_KrBusinessVendorWithoutItsNumberOrCeo_RejectsBcE522(string field)
    {
        var fields = field == "CeoName" ? KrBusiness with { CeoName = null } : KrBusiness with { BusinessRegNo = null };

        var error = Error(() => Vendor.Create("ACME", fields, krCompany: true));

        error.Code.Should().Be(Vendor.KrBusinessRequiredCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "MM-UT-872"), Trait("Rule", "BC-BR-521")]
    public void Create_KrVendorWithAWrongCheckDigit_RejectsBcE521()
    {
        Error(() => Vendor.Create("ACME", KrBusiness with { BusinessRegNo = "220-81-62518" }, krCompany: true)).Code.Should().Be("BC-E-521");
    }

    [Fact, Trait("Id", "MM-UT-872")]
    public void Create_KrIndividual_NeedsNoBusinessNumber_AndOutsideKrNothingIsChecked()
    {
        Vendor.Create("ACME", Valid with { TaxPartyType = "INDIVIDUAL" }, krCompany: true).Fields.BusinessRegNo.Should().BeNull();
        Vendor.Create("ACME", Valid with { BusinessRegNo = "12-3456789" }, krCompany: false).Fields.BusinessRegNo.Should().Be("12-3456789");
    }

    [Theory, Trait("Id", "MM-UT-871"), Trait("Rule", "BC-BR-523")]
    [InlineData("KR", "VAT10")]
    [InlineData("US", "NONE")]
    public void DefaultTaxCode_IsVat10InKr_AndNoneElsewhere(string country, string taxCode)
    {
        Vendor.DefaultTaxCode(country).Should().Be(taxCode);
    }
}

using FluentAssertions;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Customers;

namespace QuroErp.Domain.Tests.Sd.Customers;

/// <summary>Customer master rules (SD-UC-001, docs/business-rules/SD/customer.md, BC-BR-521~524).</summary>
public class CustomerTests
{
    private const string Us = "US";
    private const string Kr = "KR";
    private const string Other = "JP";
    private static readonly DateOnly Today = new(2026, 11, 5);

    // A valid KR business number: 123-45-67891
    private const string BusinessRegNo = "123-45-67891";

    private static readonly CustomerFields Valid = new(
        ShipName: "acme ship", Phone1: "555-0100", Rep: "KIM", ZipCode: "90001", Addr1: "1 Main St", City: "Springfield", State: "CA",
        TaxCodeId: 7);

    private static readonly CustomerFields KrValid = Valid with
    {
        ZipCode = "04524",
        State = null,
        TaxPartyType = "business",
        BusinessRegNo = BusinessRegNo,
        CeoName = "Hong",
    };

    private static Customer Create(CustomerFields fields, string country = Us, string? name = "  acme corp ") =>
        Customer.Create(name, fields, country, Today, Customer.ShipAndBill);

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Fact, Trait("Id", "SD-UC-001-T01"), Trait("Id", "SD-UT-301"), Trait("Id", "SD-UT-302")]
    public void Create_TrimsAndUpperCasesTheName_AndFillsTheDefaults()
    {
        var customer = Create(Valid);

        customer.CName.Should().Be("ACME CORP");
        var f = customer.Fields;
        (f.ShipName, f.Terms, f.ValidFlag, f.Country, f.ShipCountry, f.StartDt, f.CreditLimit, f.BillingFlow, f.FreightCharge)
            .Should().Be(("ACME SHIP", "COD_CHECK", "Y", "USA", "USA", (DateOnly?)Today, (decimal?)0m, "SHIP_AND_BILL", (string?)null));
    }

    [Theory, Trait("Id", "SD-UC-001-T02"), Trait("Id", "SD-UT-301")]
    [InlineData("CName")]
    [InlineData("ShipName")]
    [InlineData("Phone1")]
    [InlineData("Rep")]
    [InlineData("ZipCode")]
    public void Create_WithoutARequiredValue_IsRefused(string field)
    {
        var fields = field switch
        {
            "ShipName" => Valid with { ShipName = " " },
            "Phone1" => Valid with { Phone1 = null },
            "Rep" => Valid with { Rep = "" },
            "ZipCode" => Valid with { ZipCode = null },
            _ => Valid,
        };

        var error = FluentActions.Invoking(() => Create(fields, name: field == "CName" ? " " : "ACME"))
            .Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((Customer.RequiredCode, (object)field));
    }

    [Fact, Trait("Id", "SD-UT-301")]
    public void Change_WithoutTerms_IsRefused_ButANewCustomerGetsCodCheck()
    {
        var customer = Create(Valid);

        CodeOf(() => customer.Change(customer.Fields with { Terms = null }, Us)).Should().Be(Customer.RequiredCode);
    }

    [Fact, Trait("Id", "SD-UT-303")]
    public void NoShipAddress_CopiesTheBillingAddress_AndAShipAddressNeedsItsZipCode()
    {
        var copied = Create(Valid).Fields;

        (copied.ShipAddr1, copied.ShipCity, copied.ShipState, copied.ShipCountry, copied.ShipZipCode)
            .Should().Be(("1 Main St", "Springfield", "CA", "USA", "90001"));
        CodeOf(() => Create(Valid with { ShipAddr1 = "9 Dock Rd" })).Should().Be(Customer.ShipZipCodeRequiredCode);
        Create(Valid with { ShipAddr1 = "9 Dock Rd", ShipZipCode = "90002" }).Fields.ShipZipCode.Should().Be("90002");
    }

    [Fact, Trait("Id", "SD-UT-304")]
    public void NoPriceLevel_IsTheCustomerName()
    {
        Create(Valid).Fields.PriceLevel.Should().Be("ACME CORP");
        Create(Valid with { PriceLevel = "WHOLESALE" }).Fields.PriceLevel.Should().Be("WHOLESALE");
    }

    [Theory, Trait("Id", "SD-UT-314")]
    [InlineData("ShipName", 101)]
    [InlineData("Contact", 101)]
    [InlineData("Addr1", 101)]
    [InlineData("Phone2", 31)]
    [InlineData("ShipFax", 31)]
    [InlineData("City", 41)]
    [InlineData("Remarks", 201)]
    [InlineData("Memo3", 201)]
    public void TooLongText_IsRefused_WithTheFieldAndItsLimit(string field, int length)
    {
        var text = new string('A', length);
        var fields = field switch
        {
            "ShipName" => Valid with { ShipName = text },
            "Contact" => Valid with { Contact = text },
            "Addr1" => Valid with { Addr1 = text },
            "Phone2" => Valid with { Phone2 = text },
            "ShipFax" => Valid with { ShipFax = text },
            "City" => Valid with { City = text },
            "Remarks" => Valid with { Remarks = text },
            _ => Valid with { Memo3 = text },
        };

        var error = FluentActions.Invoking(() => Create(fields)).Should().Throw<DomainException>().Which;

        (error.Code, error.Args[0], error.Args[1]).Should().Be((Customer.InvalidTextCode, (object)field, (object)(length - 1)));
    }

    [Fact, Trait("Id", "SD-UT-314")]
    public void ANameOver100_AndABadEmail_AreRefused()
    {
        CodeOf(() => Create(Valid, name: new string('A', 101))).Should().Be(Customer.InvalidTextCode);
        CodeOf(() => Create(Valid with { Email = "not-an-email" })).Should().Be(Customer.InvalidTextCode);
        Create(Valid with { Email = "buyer@example.com" }).Fields.Email.Should().Be("buyer@example.com");
    }

    [Fact, Trait("Id", "SD-UT-331"), Trait("Id", "BC-UT-523")]
    public void AUsCustomer_NeedsItsShipToTaxCode_OthersGetTheCountryDefault()
    {
        var error = FluentActions.Invoking(() => Create(Valid with { TaxCodeId = null })).Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((Customer.UsTaxCodeRequiredCode, (object)"TaxCodeId"));
        Customer.DefaultTaxCode(Us).Should().BeNull();
        Customer.DefaultTaxCode(Kr).Should().Be("VAT10");
        Customer.DefaultTaxCode(Other).Should().Be(TaxCodeRules.NoneTaxCode);
        Create(Valid with { TaxCodeId = null }, Other).Fields.TaxCodeId.Should().BeNull("the service fills the default");
    }

    [Fact, Trait("Id", "SD-UT-332"), Trait("Id", "BC-UT-521"), Trait("Id", "BC-UT-522")]
    public void AKrBusinessCustomer_HasItsPartyNumberAndCeo_WithAFiveDigitZipAndNoState()
    {
        var customer = Create(KrValid, Kr);

        (customer.Fields.TaxPartyType, customer.Fields.BusinessRegNo, customer.Fields.Country)
            .Should().Be(("BUSINESS", "1234567891", (string?)null));
        Create(KrValid with { TaxPartyType = "INDIVIDUAL", BusinessRegNo = null, CeoName = null }, Kr).Fields.TaxPartyType
            .Should().Be("INDIVIDUAL");
    }

    [Theory, Trait("Id", "SD-UT-332"), Trait("Id", "BC-UT-521"), Trait("Id", "BC-UT-522")]
    [InlineData("party", "SD-E-332", "TaxPartyType")]
    [InlineData("regNo", "BC-E-522", "BusinessRegNo")]
    [InlineData("ceo", "BC-E-522", "CeoName")]
    [InlineData("zip", "SD-E-332", "ZipCode")]
    [InlineData("shipZip", "SD-E-332", "ShipZipCode")]
    [InlineData("state", "SD-E-332", "State")]
    public void AKrCustomer_WithAMissingOrForeignValue_IsRefused(string broken, string code, string field)
    {
        var fields = broken switch
        {
            "party" => KrValid with { TaxPartyType = "COMPANY" },
            "regNo" => KrValid with { BusinessRegNo = null },
            "ceo" => KrValid with { CeoName = " " },
            "zip" => KrValid with { ZipCode = "4524" },
            "shipZip" => KrValid with { ShipAddr1 = "Dock", ShipZipCode = "A1234" },
            _ => KrValid with { State = "CA" },
        };

        var error = FluentActions.Invoking(() => Create(fields, Kr)).Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((code, (object)field));
    }

    [Fact, Trait("Id", "BC-UT-521")]
    public void AKrBusinessNumber_WithAWrongCheckDigit_IsRefused() =>
        CodeOf(() => Create(KrValid with { BusinessRegNo = "123-45-67890" }, Kr)).Should().Be(BusinessRegistrationNo.InvalidCode);

    [Theory, Trait("Id", "SD-UT-335")]
    [InlineData(null, "WITH_SHIPMENT", "SHIP_AND_BILL")]
    [InlineData(null, "SEPARATE", "SHIP_THEN_BILL")]
    [InlineData(null, null, "SHIP_AND_BILL")]
    [InlineData("bill_then_ship", "WITH_SHIPMENT", "BILL_THEN_SHIP")]
    public void BillingFlow_DefaultsToTheCompanySetting_AndCanBeChosen(string? given, string? companyBilling, string expected) =>
        Customer.Create("ACME", Valid with { BillingFlow = given }, Us, Today, Customer.BillingFlowOf(companyBilling))
            .Fields.BillingFlow.Should().Be(expected);

    [Fact, Trait("Id", "SD-UT-335")]
    public void AnUnknownBillingFlow_IsRefused()
    {
        var error = FluentActions.Invoking(() => Create(Valid with { BillingFlow = "LATER" })).Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((Customer.BillingFlowCode, (object)"LATER"));
    }

    [Fact, Trait("Id", "SD-UT-302")]
    public void Change_KeepsTheName()
    {
        var customer = Customer.Restore(5, "ACME CORP", Create(Valid).Fields);

        customer.Change(Valid with { Phone1 = "555-0199", Terms = "NET_30", BillingFlow = "SHIP_THEN_BILL" }, Us);

        (customer.CName, customer.Fields.Phone1, customer.Fields.BillingFlow).Should().Be(("ACME CORP", "555-0199", "SHIP_THEN_BILL"));
    }

    [Fact, Trait("Id", "SD-UT-307")]
    public void ACustomerWithDocuments_CannotBeDeleted()
    {
        var customer = Customer.Restore(5, "ACME CORP", Create(Valid).Fields);

        var error = FluentActions.Invoking(() => customer.EnsureDeletable(hasDocuments: true)).Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((Customer.InUseCode, (object)"ACME CORP"));
        FluentActions.Invoking(() => customer.EnsureDeletable(hasDocuments: false)).Should().NotThrow();
    }

    [Theory, Trait("Id", "SD-UT-333"), Trait("Id", "BC-UT-524")]
    [InlineData(null, "CA", "CertificateNo")]
    [InlineData("EX-1", " ", "StateCode")]
    public void AnExemptionCertificate_NeedsItsNumberAndState(string? number, string? state, string field)
    {
        var error = FluentActions.Invoking(() => CustomerTaxExemptions.Clean(number, state, null)).Should().Throw<DomainException>().Which;

        (error.Code, error.Args.Single()).Should().Be((CustomerTaxExemptions.InvalidCode, (object)field));
        CustomerTaxExemptions.Clean(" EX-1 ", "ca", null).Should().Be(new TaxExemptionCertificate("EX-1", "CA", null));
    }

    [Fact, Trait("Id", "SD-UT-333"), Trait("Id", "BC-UT-524")]
    public void AState_HasOneValidCertificate_ExpiredOnesAreKept()
    {
        var expired = new TaxExemptionCertificate("OLD", "CA", Today.AddDays(-1));
        var open = new TaxExemptionCertificate("NEW", "CA", null);
        var other = new TaxExemptionCertificate("TX-1", "TX", Today);

        FluentActions.Invoking(() => CustomerTaxExemptions.EnsureOneValidPerState([expired, open, other], Today)).Should().NotThrow();
        var error = FluentActions.Invoking(() => CustomerTaxExemptions.EnsureOneValidPerState(
            [open, other, new TaxExemptionCertificate("NEW2", "CA", Today.AddYears(1))], Today)).Should().Throw<DomainException>().Which;
        (error.Code, error.Args.Single()).Should().Be((CustomerTaxExemptions.InvalidCode, (object)"CA"));
    }
}

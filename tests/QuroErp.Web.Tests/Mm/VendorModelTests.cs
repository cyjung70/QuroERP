using FluentAssertions;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>The vendor screen model and its requests (MM-UC-001).</summary>
public class VendorModelTests
{
    private static readonly VendorDto Dto = new(701, "ACME", "Kim", "Lee", "555-0100", "555-0101", "555-0102", "555-0109",
        "a@example.com", "NET_60", "N", 70.5m, "Memo", "1 Main St", "Suite 2", "Springfield", "CA", "USA", "90001", "9 Dock Rd",
        null, "Riverside", "NY", "USA", "10001", "GROUP", "EUR", 7, "NONE", "BUSINESS", "2208162517", "Hong", "Retail", "Tools",
        AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

    [Fact, Trait("Id", "MM-UT-851"), Trait("Rule", "MM-BR-851")]
    public void New_HasTheApiDefaults()
    {
        var model = new VendorModel();

        (model.Terms, model.ValidFlag, model.Country, model.ShipCountry, model.BalAmt).Should().Be(("NET_30", "Y", "USA", "USA", 0m));
        model.CurrencyCode.Should().BeNull();
        model.TaxCode.Should().BeNull();
    }

    [Fact, Trait("Id", "MM-UC-001-T01")]
    public void CreateRequest_CarriesEveryFieldAndTheTaxCodeId()
    {
        var request = VendorModel.From(Dto).ToCreateRequest(7);

        request.VName.Should().Be("ACME");
        request.Should().BeEquivalentTo(Dto, o => o.ExcludingMissingMembers()
            .Excluding(d => d.Id).Excluding(d => d.UpdatedAt).Excluding(d => d.Warnings));
        request.TaxCodeId.Should().Be(7);
    }

    [Fact, Trait("Id", "MM-UC-001-T03")]
    public void UpdateRequest_SendsTheUpdatedAtAsRead_AndNoTaxCodeIdForAnEmptyCode()
    {
        var model = VendorModel.From(Dto);
        model.TaxCode = null;

        var request = model.ToUpdateRequest(null);

        request.UpdatedAt.Should().Be(AuditTimes.Updated);
        request.TaxCodeId.Should().BeNull();
        request.ShipAddr1.Should().Be("9 Dock Rd");
        request.BusinessItem.Should().Be("Tools");
    }
}

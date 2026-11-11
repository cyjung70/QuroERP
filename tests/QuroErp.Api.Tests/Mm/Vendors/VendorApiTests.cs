using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Api.Tests.Bc.Tax;
using QuroErp.Application.Bc.Localization;
using QuroErp.Contracts.Mm.Vendors;
using static QuroErp.Api.Tests.Mm.Vendors.InMemoryVendorRepository;

namespace QuroErp.Api.Tests.Mm.Vendors;

/// <summary>/api/mm/vendors (MM-UC-001). The factory company is KR. Saves use their own factory.</summary>
public class VendorApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CreateVendorRequest New = new()
    {
        VName = " new supply ",
        Phone1 = "555-0100",
        ZipCode = "06236",
        TaxPartyType = "INDIVIDUAL",
    };

    [Fact, Trait("Id", "MM-UC-001")]
    public async Task SearchGetAndAdjacent_ForAnySignedInCaller_InNameOrder()
    {
        var client = factory.NonAdmin();

        var list = await client.GetFromJsonAsync<VendorListDto>(VendorRoutes.Base + "?vName=e");
        var acme = await client.GetFromJsonAsync<VendorDto>($"{VendorRoutes.Base}/{AcmeId}");
        using var next = await client.GetAsync($"{VendorRoutes.Base}/{AcmeId}/{VendorRoutes.Next}");
        using var previous = await client.GetAsync($"{VendorRoutes.Base}/{AcmeId}/{VendorRoutes.Previous}");
        using var missing = await client.GetAsync($"{VendorRoutes.Base}/999");

        list!.Items.Select(v => v.VName).Should().Equal("ACME", "BETA");
        list.Total.Should().Be(2);
        acme!.BalAmt.Should().Be(70.5m, "MM-BR-858");
        acme.Warnings.Should().BeEmpty();
        (await next.Content.ReadFromJsonAsync<VendorDto>())!.VName.Should().Be("BETA");
        previous.StatusCode.Should().Be(HttpStatusCode.NoContent);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Id", "MM-UC-001")]
    public async Task Save_WithoutFullAccess_Returns403_AndWithoutToken_401()
    {
        using var nonAdmin = await factory.NonAdmin().PostAsJsonAsync(VendorRoutes.Base, New);
        using var anonymous = await factory.CreateClient().GetAsync(VendorRoutes.Base);

        nonAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "MM-UC-001-T01"), Trait("Rule", "MM-BR-851"), Trait("Rule", "MM-BR-871")]
    public async Task Create_Returns201_WithDefaults_AndTheKrDefaultTaxCode()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(VendorRoutes.Base, New with { Addr1 = "1 Main St", State = "CA" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var vendor = (await response.Content.ReadFromJsonAsync<VendorDto>())!;
        vendor.VName.Should().Be("NEW SUPPLY");
        (vendor.Terms, vendor.ValidFlag, vendor.Country).Should().Be(("NET_30", "Y", "USA"));
        (vendor.ShipAddr1, vendor.ShipState, vendor.ShipZipCode).Should().Be(("1 Main St", "CA", "06236"));
        vendor.VendorPrice.Should().Be("NEW SUPPLY");
        (vendor.TaxCodeId, vendor.TaxCode).Should().Be((InMemoryTaxCodeRepository.Vat10Id, "VAT10"));
        vendor.CurrencyCode.Should().BeNull("an empty currency is the base currency");
        vendor.CreatedBy.Should().Be(ApiFactory.UserId);
        response.Headers.Location!.ToString().Should().EndWith($"/{VendorRoutes.Base}/{vendor.Id}");
    }

    [Fact, Trait("Id", "MM-UT-871"), Trait("Rule", "BC-BR-523")]
    public async Task Create_InAUsCompany_GetsTheNoneTaxCode_AndNoKrChecks()
    {
        await using var own = new ApiFactory();
        own.Localization.Company = new CompanyLocalizationRow("US", "USD", "en", "DOCUMENT", "America/New_York");
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(VendorRoutes.Base, New with { TaxPartyType = null, CurrencyCode = "usd" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var vendor = (await response.Content.ReadFromJsonAsync<VendorDto>())!;
        (vendor.TaxCode, vendor.CurrencyCode, vendor.TaxPartyType).Should().Be(("NONE", "USD", null));
    }

    [Theory, Trait("Id", "MM-UT-871"), Trait("Rule", "BC-BR-415"), Trait("Rule", "BC-BR-523")]
    [InlineData("EUR", null, "BC-E-415", "EUR")]
    [InlineData(null, 999L, "BC-E-523", "999")]
    public async Task Create_AnUnusableCurrencyOrTaxCode_Returns422(string? currency, long? taxCodeId, string code, string arg)
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(VendorRoutes.Base, New with { CurrencyCode = currency, TaxCodeId = taxCodeId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].ToString().Should().Be(arg);
    }

    [Fact, Trait("Id", "MM-UC-001-T01"), Trait("Rule", "BC-BR-522")]
    public async Task Create_ABusinessNumberAnotherVendorHas_IsSavedWithAWarning()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(
            VendorRoutes.Base, New with { TaxPartyType = "BUSINESS", BusinessRegNo = "220-81-62517", CeoName = "Kim" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var vendor = (await response.Content.ReadFromJsonAsync<VendorDto>())!;
        vendor.BusinessRegNo.Should().Be(BusinessRegNo);
        var warning = vendor.Warnings.Should().ContainSingle().Which;
        warning.Code.Should().Be("BC-W-522");
        warning.Args.Should().Equal("BETA");
    }

    [Fact, Trait("Id", "MM-UC-001-T02"), Trait("Rule", "MM-BR-851")]
    public async Task Create_WithoutPhone1_Returns422MmE851()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(VendorRoutes.Base, New with { Phone1 = " " });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-851");
        problem.GetProperty("args")[0].GetString().Should().Be("Phone1");
    }

    [Fact, Trait("Id", "MM-UT-852"), Trait("Rule", "MM-BR-852")]
    public async Task Create_ATakenName_Returns422MmE852()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(VendorRoutes.Base, New with { VName = " acme " });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-852");
        problem.GetProperty("args")[0].GetString().Should().Be("ACME");
    }

    [Fact, Trait("Id", "MM-UC-001-T04"), Trait("Id", "MM-UT-855"), Trait("Rule", "MM-BR-855")]
    public async Task Create_AnUnknownCodeValue_Returns422CommonE002()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var terms = await client.PostAsJsonAsync(VendorRoutes.Base, New with { Terms = "NET 99" });
        using var shipState = await client.PostAsJsonAsync(VendorRoutes.Base, New with { ShipAddr1 = "Dock", ShipZipCode = "1", ShipState = "ZZ" });

        foreach (var response in new[] { terms, shipState })
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-002");
        }
    }

    [Fact, Trait("Id", "MM-UC-001"), Trait("Rule", "MM-BR-852")]
    public async Task Update_Returns200_AndKeepsTheName()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            $"{VendorRoutes.Base}/{AcmeId}",
            new UpdateVendorRequest { Phone1 = "555-0199", ZipCode = "1", Terms = "NET_60", TaxPartyType = "INDIVIDUAL", UpdatedAt = Created });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var vendor = (await response.Content.ReadFromJsonAsync<VendorDto>())!;
        (vendor.VName, vendor.Phone1, vendor.Terms, vendor.UpdatedBy).Should().Be(("ACME", "555-0199", "NET_60", ApiFactory.UserId));
        vendor.BalAmt.Should().Be(70.5m);
    }

    [Fact, Trait("Id", "MM-UC-001-T03")]
    public async Task Update_AStaleVendor_Returns409CommonE004()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            $"{VendorRoutes.Base}/{AcmeId}",
            new UpdateVendorRequest { Phone1 = "1", ZipCode = "1", TaxPartyType = "INDIVIDUAL", UpdatedAt = Created.AddSeconds(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "MM-UT-857"), Trait("Rule", "MM-BR-857")]
    public async Task Delete_AVendorInUse_Returns422MmE857_AndAnUnusedOne204()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();
        var updatedAt = Uri.EscapeDataString(Created.ToString("O"));

        using var used = await client.DeleteAsync($"{VendorRoutes.Base}/{AcmeId}?updatedAt={updatedAt}");
        using var unused = await client.DeleteAsync($"{VendorRoutes.Base}/{BetaId}?updatedAt={updatedAt}");
        using var gone = await client.GetAsync($"{VendorRoutes.Base}/{BetaId}");

        used.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await used.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-857");
        problem.GetProperty("args")[0].GetString().Should().Be("ACME");
        unused.StatusCode.Should().Be(HttpStatusCode.NoContent);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

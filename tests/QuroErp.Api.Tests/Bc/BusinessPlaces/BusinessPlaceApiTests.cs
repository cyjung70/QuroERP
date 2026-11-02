using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Contracts.Bc.Companies;
using static QuroErp.Api.Tests.ApiFactory.InMemoryBusinessPlaceRepository;

namespace QuroErp.Api.Tests.Bc.BusinessPlaces;

/// <summary>/api/bc/business-places (BC-BR-319, BC-BR-320). Each test has its own factory.</summary>
public class BusinessPlaceApiTests
{
    private static readonly UpdateBusinessPlaceRequest Request = new(
        "Quro Seoul", "220-81-62517", "0001", "Kim", "Service", "Software", "1 Sejong-daero", null, "Seoul", null, "04524", "02-000-0000",
        true, Created);

    [Fact, Trait("Id", "BC-UT-319"), Trait("Rule", "BC-BR-319")]
    public async Task List_ForAnySignedInCaller_ReturnsTheDefaultPlace()
    {
        await using var factory = new ApiFactory();

        var places = await factory.NonAdmin().GetFromJsonAsync<List<BusinessPlaceDto>>(BusinessPlaceRoutes.Base);

        places!.Should().ContainSingle().Which.Should().Match<BusinessPlaceDto>(p => p.BusinessPlaceCode == "MAIN" && p.IsDefault);
    }

    [Theory, Trait("Id", "BC-UT-319"), Trait("Rule", "BC-BR-319")]
    [InlineData("POST", BusinessPlaceRoutes.Base)]
    [InlineData("DELETE", BusinessPlaceRoutes.Base + "/1")]
    public async Task AddingOrDeletingAPlace_Returns405(string method, string route)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    public async Task Update_SavesTheSupplierValues_WithTheRegistrationNumberWithoutHyphens()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(BusinessPlaceRoutes.For(1), Request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<BusinessPlaceDto>();
        (saved!.BusinessPlaceName, saved.BusinessRegNo, saved.SubBusinessNo, saved.BusinessItem, saved.UpdatedBy)
            .Should().Be(("Quro Seoul", "2208162517", "0001", "Software", ApiFactory.UserId));
        saved.BusinessPlaceCode.Should().Be("MAIN", "the code does not change");
    }

    [Theory, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    [InlineData(null, "0001", "BusinessRegNo")]
    [InlineData("220-81-62518", "0001", "BusinessRegNo")]
    [InlineData("2208162517", "01", "SubBusinessNo")]
    public async Task Update_KrWithABadNumber_Returns422BcE320(string? regNo, string subNo, string field)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(BusinessPlaceRoutes.For(1), Request with { BusinessRegNo = regNo, SubBusinessNo = subNo });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-320");
        problem.GetProperty("args")[0].GetString().Should().Be(field);
    }

    [Fact, Trait("Id", "BC-UT-320")]
    public async Task Update_WithAStaleUpdatedAt_Returns409()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(BusinessPlaceRoutes.For(1), Request with { UpdatedAt = Created.AddDays(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UT-305")]
    public async Task Update_WithoutFullAccess_Returns403()
    {
        await using var factory = new ApiFactory();

        using var response = await factory.NonAdmin().PutAsJsonAsync(BusinessPlaceRoutes.For(1), Request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.BusinessPlaces.Places[0].BusinessPlaceName.Should().Be("Quro");
    }

    [Fact, Trait("Id", "BC-UT-315"), Trait("Rule", "BC-BR-315")]
    public async Task Company_NoLongerReturnsTheSupplierValues()
    {
        await using var factory = new ApiFactory();

        using var response = await factory.NonAdmin().GetAsync(CompanyRoutes.Base);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("registerNo").And.NotContain("businessType").And.NotContain("businessItem");
    }
}

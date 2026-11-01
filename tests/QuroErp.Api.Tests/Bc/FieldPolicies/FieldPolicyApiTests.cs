using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.FieldPolicies;

namespace QuroErp.Api.Tests.Bc.FieldPolicies;

/// <summary>/api/bc/field-policies (BC-BR-431, BC-BR-433, BC-BR-435) with the built-in country packs. Each test has its own factory.</summary>
public class FieldPolicyApiTests
{
    [Fact, Trait("Id", "BC-UT-430"), Trait("Rule", "BC-BR-430")]
    public async Task Get_ForAnySignedInCaller_MergesTheKrPackOfTheKrCompany()
    {
        await using var factory = new ApiFactory();

        var fields = await factory.NonAdmin().GetFromJsonAsync<List<FieldPolicyDto>>(FieldPolicyRoutes.ForScreen("Sd.Customer"));

        fields!.Single(f => f.FieldKey == "Sd.Customer.State").Should().Be(new FieldPolicyDto("Sd.Customer.State", "HIDDEN", null, "Sd.Customer.State", "CountryPack"));
        fields!.Single(f => f.FieldKey == "Sd.Customer.Country").DefaultValue.Should().Be("KR");
        fields!.Single(f => f.FieldKey == "Sd.Customer.Email").Source.Should().Be("System");
    }

    [Theory, Trait("Id", "BC-UT-431")]
    [InlineData(FieldPolicyRoutes.Base, HttpStatusCode.BadRequest, "Common-E-005")]
    [InlineData(FieldPolicyRoutes.Base + "?screen=Sd.Nothing", HttpStatusCode.NotFound, "Common-E-006")]
    public async Task Get_WithoutOrWithAnUnknownScreen_Fails(string route, HttpStatusCode status, string code)
    {
        await using var factory = new ApiFactory();

        using var response = await factory.NonAdmin().GetAsync(route);

        response.StatusCode.Should().Be(status);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact, Trait("Id", "BC-UT-431"), Trait("Rule", "BC-BR-431")]
    public async Task Save_TheCompanyOverrideCoversThePack()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            FieldPolicyRoutes.Base, new SaveFieldPoliciesRequest([new("Sd.Customer.State", "OPTIONAL", null, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<FieldPolicyOverrideDto>>())!.Should().ContainSingle()
            .Which.Should().Match<FieldPolicyOverrideDto>(o => o.State == "OPTIONAL" && o.UpdatedBy == ApiFactory.UserId);
        var fields = await client.GetFromJsonAsync<List<FieldPolicyDto>>(FieldPolicyRoutes.ForScreen("Sd.Customer"));
        fields!.Single(f => f.FieldKey == "Sd.Customer.State").Should().Match<FieldPolicyDto>(f => f.State == "OPTIONAL" && f.Source == "Company");
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public async Task Save_HidingARequiredBusinessField_Returns422BcE433_AndSavesNothing()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(FieldPolicyRoutes.Base, new SaveFieldPoliciesRequest(
            [new("Sd.Customer.State", "OPTIONAL", null, null), new("Sd.Customer.CName", "HIDDEN", null, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-433");
        problem.GetProperty("args")[0].GetString().Should().Be("Sd.Customer.CName");
        factory.FieldPolicies.Rows.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-435"), Trait("Rule", "BC-BR-435")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        await using var factory = new ApiFactory();

        using var response = await factory.NonAdmin().PutAsJsonAsync(
            FieldPolicyRoutes.Base, new SaveFieldPoliciesRequest([new("Sd.Customer.State", "OPTIONAL", null, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.FieldPolicies.Rows.Should().BeEmpty();
    }
}

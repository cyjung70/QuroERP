using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Codes;
using static QuroErp.Api.Tests.Bc.Codes.InMemoryCodeMinorRepository;

namespace QuroErp.Api.Tests.Bc.Codes;

/// <summary>/api/bc/codes (BC-UC-008, SD-BR-905, SD-BR-910~916, BC-BR-214). Saves use their own factory so the shared codes stay as seeded.</summary>
public class CodeMinorApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UC-008-T03")]
    public async Task Search_ForAnySignedInCaller_ReturnsTheMajorsCodesWithNamesInTheLanguage()
    {
        var codes = await factory.NonAdmin().GetFromJsonAsync<List<CodeMinorDto>>(CodeMinorRoutes.Base + "?majorCode=unit&lang=ko");

        codes.Should().NotBeNull();
        codes!.Select(c => (c.MinorCode, c.Name)).Should().Equal(("EA", "개"), ("BOX", "Box"));
        codes![0].Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Each", ["ko"] = "개" });
    }

    [Theory, Trait("Id", "SD-UT-910")]
    [InlineData("", HttpStatusCode.BadRequest, "Common-E-005")]
    [InlineData("?majorCode=NOPE", HttpStatusCode.NotFound, "Common-E-003")]
    public async Task Search_WithoutOrWithAnUnknownMajor_Fails(string query, HttpStatusCode status, string code)
    {
        using var response = await factory.NonAdmin().GetAsync(CodeMinorRoutes.Base + query);

        response.StatusCode.Should().Be(status);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Search_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(CodeMinorRoutes.Base + "?majorCode=UNIT");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest("UNIT", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

public class CodeMinorSaveApiTests
{
    [Fact, Trait("Id", "BC-UC-008-T01")]
    public async Task Save_AddsChangesAndDeletes_AndReturnsEveryCodeOfTheMajor()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();
        await client.PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest(
            "UNIT", [new CodeMinorInsert("TMP", new Dictionary<string, string?> { ["en"] = "Temp" }, 90, null, null, null)], null, null));
        var tmp = (await client.GetFromJsonAsync<List<CodeMinorDto>>(CodeMinorRoutes.Base + "?majorCode=UNIT"))!.Single(c => c.MinorCode == "TMP");

        using var response = await client.PutAsJsonAsync(CodeMinorRoutes.Base + "?lang=ko", new SaveCodeMinorsRequest(
            "unit",
            [new CodeMinorInsert("pcs", new Dictionary<string, string?> { ["en"] = "Pieces", ["ko"] = "개수" }, 5, null, "P", null)],
            [new CodeMinorUpdate(BoxId, "BOX", new Dictionary<string, string?> { ["en"] = "Box", ["ko"] = "상자" }, 30, false, null, null, Created)],
            [new CodeMinorDelete(tmp.Id, tmp.UpdatedAt)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var codes = await response.Content.ReadFromJsonAsync<List<CodeMinorDto>>();
        codes!.Select(c => (c.MinorCode, c.Name, c.SortOrder, c.UseYn)).Should().Equal(
            ("PCS", "개수", 5, true), ("EA", "개", 10, true), ("BOX", "상자", 30, false));
    }

    [Theory, Trait("Id", "BC-UC-008-T02")]
    [InlineData("SD-E-911", "bad-code", 50)]
    [InlineData("SD-E-913", "NEW", 10)]
    public async Task Save_WithAFailingRow_Returns422WithTheRowsCode_AndSavesNothing(string code, string minorCode, int sortOrder)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest(
            "UNIT",
            [
                new CodeMinorInsert("OK", new Dictionary<string, string?> { ["en"] = "Fine" }, 40, null, null, null),
                new CodeMinorInsert(minorCode, new Dictionary<string, string?> { ["en"] = "Name" }, sortOrder, null, null, null),
            ],
            null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().BeOneOf(minorCode, minorCode.ToUpperInvariant());
        var codes = await client.GetFromJsonAsync<List<CodeMinorDto>>(CodeMinorRoutes.Base + "?majorCode=UNIT");
        codes!.Select(c => c.MinorCode).Should().Equal("EA", "BOX");
    }

    [Theory, Trait("Id", "SD-UT-914")]
    [InlineData(BoxId, "SD-E-914", "BOX")]
    [InlineData(EaId, "SD-E-916", "EA")]
    public async Task Save_DeletingAStoredOrConstantCode_Returns422(long id, string code, string minorCode)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest("UNIT", null, null, [new CodeMinorDelete(id, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(minorCode);
    }

    [Fact, Trait("Id", "SD-UT-912")]
    public async Task Save_ChangingTheCode_Returns422SdE912()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest(
            "UNIT", null, [new CodeMinorUpdate(BoxId, "CASE", new Dictionary<string, string?> { ["en"] = "Box" }, 20, true, null, null, Created)], null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("SD-E-912");
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public async Task Save_WithAStaleRow_Returns409()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMinorRoutes.Base, new SaveCodeMinorsRequest(
            "UNIT", null, [new CodeMinorUpdate(BoxId, null, new Dictionary<string, string?> { ["en"] = "Box" }, 20, true, null, null, Created.AddSeconds(-1))],
            null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }
}

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Codes;
using static QuroErp.Api.Tests.Bc.Codes.InMemoryCodeMajorRepository;

namespace QuroErp.Api.Tests.Bc.Codes;

/// <summary>/api/sys/code-majors (BC-UC-023, SD-BR-901~905, BC-BR-214). Saves use their own factory so the shared Majors stay as seeded.</summary>
public class CodeMajorApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UC-023-T07")]
    public async Task Search_ForAnySignedInCaller_ReturnsMajorsWithNamesInTheLanguageAndMinorCount()
    {
        var majors = await factory.NonAdmin().GetFromJsonAsync<List<CodeMajorDto>>(CodeMajorRoutes.Base + "?lang=ko");
        var te = await factory.NonAdmin().GetFromJsonAsync<List<CodeMajorDto>>(CodeMajorRoutes.Base + "?majorCode=te");

        majors.Should().NotBeNull();
        majors!.Select(m => (m.MajorCode, m.Name, m.MinorCount)).Should().Equal(("TEMP", "Temporary", 0), ("TERMS", "Terms", 1), ("UNIT", "단위", 2));
        majors![2].Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Unit", ["ko"] = "단위" });
        te!.Select(m => m.MajorCode).Should().Equal("TEMP", "TERMS");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Search_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(CodeMajorRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

public class CodeMajorSaveApiTests
{
    [Fact, Trait("Id", "BC-UC-023-T07")]
    public async Task Save_AddsChangesAndDeletes_AndReturnsEveryMajor()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base + "?lang=ko", new SaveCodeMajorsRequest(
            [new CodeMajorInsert("ship_via", new Dictionary<string, string?> { ["en"] = "Ship Via", ["ko"] = "운송 방법" }, null)],
            [new CodeMajorUpdate(TermsId, "TERMS", new Dictionary<string, string?> { ["en"] = "Terms", ["ko"] = "결제 조건" }, false, Created)],
            [new CodeMajorDelete(TempId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var majors = await response.Content.ReadFromJsonAsync<List<CodeMajorDto>>();
        majors!.Select(m => (m.MajorCode, m.Name, m.UseYn)).Should().Equal(("SHIP_VIA", "운송 방법", true), ("TERMS", "결제 조건", false), ("UNIT", "단위", true));
    }

    [Theory, Trait("Id", "BC-UC-023-T01")]
    [InlineData("bad-code", "SD-E-901", "bad-code")]
    [InlineData("terms", "SD-E-901", "TERMS")]
    public async Task Save_ABadOrDuplicateCode_Returns422WithTheCode(string majorCode, string code, string arg)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(
            [new CodeMajorInsert(majorCode, new Dictionary<string, string?> { ["en"] = "Name" }, null)], null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(arg);
    }

    [Fact, Trait("Id", "BC-UC-023-T02")]
    public async Task Save_ChangingTheCode_Returns422SdE902()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(
            null, [new CodeMajorUpdate(TermsId, "PAY_TERMS", new Dictionary<string, string?> { ["en"] = "Terms" }, true, Created)], null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("SD-E-902");
    }

    [Theory, Trait("Id", "BC-UC-023-T03")]
    [InlineData(TermsId, "SD-E-903", "TERMS")]
    [InlineData(UnitId, "SD-E-904", "UNIT")]
    public async Task Save_DeletingAMajorWithMinorsOrAConstantMajor_Returns422(long id, string code, string majorCode)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(id, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(majorCode);
    }

    [Fact, Trait("Id", "BC-UC-023-T04")]
    public async Task Save_SettingAConstantMajorNotInUse_Returns422SdE904()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(
            null, [new CodeMajorUpdate(UnitId, null, new Dictionary<string, string?> { ["en"] = "Unit" }, false, Created)], null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("SD-E-904");
    }

    [Fact, Trait("Id", "BC-UC-023-T05")]
    public async Task Save_WithOneFailingRowOfThree_SavesNothing()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(
            [new CodeMajorInsert("SHIP_VIA", new Dictionary<string, string?> { ["en"] = "Ship Via" }, null)],
            [new CodeMajorUpdate(TempId, null, new Dictionary<string, string?> { ["en"] = "Temp" }, true, Created)],
            [new CodeMajorDelete(TermsId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var majors = await client.GetFromJsonAsync<List<CodeMajorDto>>(CodeMajorRoutes.Base + "?lang=en");
        majors!.Select(m => (m.MajorCode, m.Name)).Should().Equal(("TEMP", "Temporary"), ("TERMS", "Terms"), ("UNIT", "Unit"));
    }

    [Fact, Trait("Id", "BC-UC-023-T06")]
    public async Task Save_ADatabaseError_Returns500WithoutTheDatabaseText()
    {
        await using var factory = new ApiFactory();
        factory.CodeMajors.FailingCode = "SHIP_VIA";
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(
            [new CodeMajorInsert("SHIP_VIA", new Dictionary<string, string?> { ["en"] = "Ship Via" }, null)], null, null));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("duplicate key").And.NotContain("uq_sys_code_major");
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public async Task Save_WithAStaleRow_Returns409()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CodeMajorRoutes.Base, new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(TempId, Created.AddSeconds(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }
}

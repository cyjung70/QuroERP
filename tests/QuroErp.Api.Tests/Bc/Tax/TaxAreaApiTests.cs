using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Tax;
using static QuroErp.Api.Tests.Bc.Tax.InMemoryTaxAreaRepository;

namespace QuroErp.Api.Tests.Bc.Tax;

/// <summary>/api/bc/tax-areas (BC-UC-029, BC-BR-507, BC-BR-511, BC-BR-512). Saves use their own factory so the shared areas stay as seeded.</summary>
public class TaxAreaApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Search_ForAnySignedInCaller_ReturnsAreasInCodeOrder_WithTheRateValidToday()
    {
        var client = factory.NonAdmin();

        var all = await client.GetFromJsonAsync<List<TaxAreaSummaryDto>>(TaxAreaRoutes.Base);
        var ny = await client.GetFromJsonAsync<List<TaxAreaSummaryDto>>(TaxAreaRoutes.Base + "?stateCode=ny");

        // LA's 2099 line has not started yet
        all!.Select(a => (a.TaxAreaCode, a.StateCode, a.EffectiveFrom, a.Rate)).Should().Equal(
            ("LA", "CA", Jan1, 9.5m), ("NYC", "NY", Jan1, 8.875m));
        ny!.Select(a => a.TaxAreaCode).Should().Equal("NYC");
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Get_ReturnsTheLinesInStartDateOrder_Or404()
    {
        var client = factory.NonAdmin();

        var la = await client.GetFromJsonAsync<TaxAreaDto>($"{TaxAreaRoutes.Base}/{LaId}");
        using var missing = await client.GetAsync($"{TaxAreaRoutes.Base}/999");

        la!.Rates.Should().Equal(new TaxAreaRateDto(Jan1, 9.5m), new TaxAreaRateDto(new DateOnly(2099, 1, 1), 10.25m));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-006");
    }

    [Fact, Trait("Id", "BC-UT-507"), Trait("Rule", "BC-BR-507")]
    public async Task Create_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(TaxAreaRoutes.Base, Request("SF", "CA"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Create_Returns201_WithTheCleanCodeAndTheLinesInOrder()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(
            TaxAreaRoutes.Base, Request(" sf ", "ca", new SaveTaxAreaRateRow(Jul1, 8.625m), new SaveTaxAreaRateRow(Jan1, 8.5m)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var area = await response.Content.ReadFromJsonAsync<TaxAreaDto>();
        area!.Should().BeEquivalentTo(new { TaxAreaCode = "SF", StateCode = "CA" });
        area.Rates.Should().Equal(new TaxAreaRateDto(Jan1, 8.5m), new TaxAreaRateDto(Jul1, 8.625m));
        response.Headers.Location!.ToString().Should().EndWith($"/{area.Id}");
    }

    [Theory, Trait("Id", "BC-UC-029-T01"), Trait("Rule", "BC-BR-511"), Trait("Rule", "BC-BR-512")]
    [InlineData("NYC", "NY", "2026-07-01", "BC-E-511")]
    [InlineData("SF", "ZZ", "2026-07-01", "Common-E-002")]
    [InlineData("SF", "CA", "2026-01-01", "BC-E-512")]
    public async Task Create_ATakenCode_AnUnknownState_OrAStartDateTwice_Returns422(string code, string state, string secondStart, string error)
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(TaxAreaRoutes.Base, Request(
            code, state, new SaveTaxAreaRateRow(Jan1, 8.5m), new SaveTaxAreaRateRow(DateOnly.Parse(secondStart, System.Globalization.CultureInfo.InvariantCulture), 9m)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(error);
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Update_ReplacesTheLines_AndAStaleUpdatedAtReturns409()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        var change = Request("NYC", "NY", new SaveTaxAreaRateRow(Jul1, 8.9m)) with { TaxAreaName = "New York", UpdatedAt = Created };

        using var saved = await client.PutAsJsonAsync($"{TaxAreaRoutes.Base}/{NycId}", change);
        using var stale = await client.PutAsJsonAsync($"{TaxAreaRoutes.Base}/{NycId}", Request("NYC", "NY") with { UpdatedAt = Created });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var area = await saved.Content.ReadFromJsonAsync<TaxAreaDto>();
        area!.TaxAreaName.Should().Be("New York");
        area.Rates.Should().Equal(new TaxAreaRateDto(Jul1, 8.9m));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Delete_AnAreaTaxCodesUse_Returns409BcE511_AndAnUnusedOneReturns204()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var used = await client.DeleteAsync($"{TaxAreaRoutes.Base}/{LaId}?updatedAt={Uri.EscapeDataString(Created.ToString("O"))}");
        using var free = await client.DeleteAsync($"{TaxAreaRoutes.Base}/{NycId}?updatedAt={Uri.EscapeDataString(Created.ToString("O"))}");

        used.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await used.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-511");
        problem.GetProperty("args")[0].GetString().Should().Be("LA");
        free.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static SaveTaxAreaRequest Request(string code, string state, params SaveTaxAreaRateRow[] rates) =>
        new(code, "Test area", state, rates, null);
}

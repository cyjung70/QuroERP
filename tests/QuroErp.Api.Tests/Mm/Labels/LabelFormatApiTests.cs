using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>Label format API (MM-UC-036, TASK-MM-055) on the in-memory formats of <see cref="InMemoryLabelFormatRepository"/>.</summary>
public class LabelFormatApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] LocationFields = ["locationCode", "warehouseCode"];

    private static BatchRequest<LabelFormatSaveRow, LabelFormatKey> Batch(params LabelFormatSaveRow[] updated) => new([], updated, []);

    private static LabelFormatSaveRow Location(decimal width, DateTimeOffset updatedAt) =>
        new("LOCATION", "PDF", width, 40, 300, LocationFields, true, updatedAt);

    [Fact, Trait("Id", "MM-UC-036-T01")]
    public async Task List_ForAnySignedInCaller_ReturnsTheFourFormats_AndNeedsSignIn()
    {
        var list = await factory.NonAdmin().GetFromJsonAsync<List<LabelFormatDto>>(LabelFormatRoutes.Base);
        using var anonymous = await factory.CreateClient().GetAsync(LabelFormatRoutes.Base);

        list!.Select(f => (f.LabelType, f.Fields.Count > 0)).Should().Equal(
            ("PALLET", true), ("ITEM", true), ("LOT", true), ("LOCATION", true));
        list![2].AvailableFields.Should().Equal("itemCd", "itemName", "lotNo", "expiryDt");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public async Task Save_AsAdmin_SavesTheRow_AndAStaleRowReturns409()
    {
        var client = await factory.AdminAsync();
        var stored = (await client.GetFromJsonAsync<List<LabelFormatDto>>(LabelFormatRoutes.Base))!.Single(f => f.LabelType == "LOCATION");

        using var saved = await client.PostAsJsonAsync(LabelFormatRoutes.Batch, Batch(Location(80, stored.UpdatedAt)));
        using var stale = await client.PostAsJsonAsync(LabelFormatRoutes.Batch, Batch(Location(90, stored.UpdatedAt)));

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<List<LabelFormatDto>>())!.Single(f => f.LabelType == "LOCATION")
            .Should().BeEquivalentTo(new { OutputKind = "PDF", WidthMm = 80m, HeightMm = 40m, Dpi = 300, Fields = LocationFields });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "MM-UC-036-T02"), Trait("Rule", "MM-BR-917")]
    public async Task Save_WithASizeOf0_Returns422MmE917()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(LabelFormatRoutes.Batch, Batch(Location(0, InMemoryLabelFormatRepository.Stored)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-917");
        problem.GetProperty("args")[0].GetString().Should().Be("LOCATION");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(
            LabelFormatRoutes.Batch, Batch(Location(80, InMemoryLabelFormatRepository.Stored)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "MM-UC-036-T03"), Trait("Rule", "MM-BR-918")]
    public async Task Preview_ReturnsAPdf_ThePalletWithTwoPages_And404ForAnUnknownType()
    {
        var client = factory.NonAdmin();

        using var pallet = await client.GetAsync(LabelFormatRoutes.PreviewOf("pallet"));
        using var unknown = await client.GetAsync(LabelFormatRoutes.PreviewOf("BOX"));

        pallet.StatusCode.Should().Be(HttpStatusCode.OK);
        pallet.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pallet.Content.ReadAsStringAsync()).Should().Be("%PDF-fake pages=2");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-006");
    }
}

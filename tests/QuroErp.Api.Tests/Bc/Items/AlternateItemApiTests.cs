using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Items;
using static QuroErp.Api.Tests.Bc.Items.InMemoryAlternateItemRepository;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>/api/bc/alternate-items (BC-UC-012, MM-BR-811~813). Saves use their own factory so the shared groups stay as seeded.</summary>
public class AlternateItemApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Batch = AlternateItemRoutes.Base + "/" + AlternateItemRoutes.Batch;

    [Fact, Trait("Id", "BC-UC-012-T03")]
    public async Task Get_ForAnySignedInCaller_ReturnsTheGroupOfTheCode_WithItsItemsInCodeOrder()
    {
        var group = await factory.NonAdmin().GetFromJsonAsync<AlternateItemGroupDto>(AlternateItemRoutes.Base + "?groupCode=BOLT");

        group!.Should().BeEquivalentTo(new { Id = BoltId, AltItemGroupCd = "BOLT", AltItemGroupName = "Bolts", UpdatedAt = Created });
        group.Items.Select(i => (i.Id, i.ItemCd)).Should().Equal((510L, "A510"), (511L, "A511"));
    }

    [Theory, Trait("Id", "BC-UC-012-T03")]
    [InlineData("?groupCode=ZZ", HttpStatusCode.NotFound, "Common-E-006")]
    [InlineData("", HttpStatusCode.UnprocessableEntity, "MM-E-802")]
    public async Task Get_AnUnknownOrMissingCode_Returns404Or422(string query, HttpStatusCode status, string code)
    {
        using var response = await factory.NonAdmin().GetAsync(AlternateItemRoutes.Base + query);

        response.StatusCode.Should().Be(status);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact, Trait("Id", "BC-UC-012")]
    public async Task Get_WithoutToken_Returns401_AndSaveWithoutFullAccess_Returns403()
    {
        using var anonymous = await factory.CreateClient().GetAsync(AlternateItemRoutes.Base + "?groupCode=BOLT");
        using var nonAdmin = await factory.NonAdmin().PostAsJsonAsync(Batch, Request(["B520"]));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UC-012-T01"), Trait("Rule", "MM-BR-811")]
    public async Task Save_AddChangeAndDelete_Returns200WithTheSavedGroup()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request([" b520 "], [new(511, "A510")], [510]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var group = await response.Content.ReadFromJsonAsync<AlternateItemGroupDto>();
        group!.Items.Select(i => i.ItemCd).Should().Equal("A510", "B520");
        group.UpdatedAt.Should().BeAfter(Created);
    }

    [Fact, Trait("Id", "BC-UC-012-T02"), Trait("Rule", "MM-BR-812"), Trait("Rule", "MM-BR-813")]
    public async Task Save_ABadRow_Returns422MmE812_AndSavesNothing()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request(["B520", "S522"], deleted: [510]));
        var after = await client.GetFromJsonAsync<AlternateItemGroupDto>(AlternateItemRoutes.Base + "?groupCode=BOLT");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-812");
        problem.GetProperty("args")[0].GetString().Should().Be("S522");
        after!.Items.Select(i => i.ItemCd).Should().Equal("A510", "A511");
    }

    [Fact, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    public async Task Save_AnItemOfAnotherGroup_Returns409MmE812_ThenMovesItOnceConfirmed()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var unconfirmed = await client.PostAsJsonAsync(Batch, Request(["B521"]));
        using var confirmed = await client.PostAsJsonAsync(Batch, Request(["B521"], moveConfirmed: true));
        var nut = await client.GetFromJsonAsync<AlternateItemGroupDto>(AlternateItemRoutes.Base + "?groupCode=NUT");

        unconfirmed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await unconfirmed.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-812");
        problem.GetProperty("args").EnumerateArray().Select(a => a.GetString()).Should().Equal("B521", "NUT");
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        nut!.Items.Should().BeEmpty();
        nut.UpdatedAt.Should().BeAfter(Created);
    }

    [Fact, Trait("Id", "BC-UC-012-T01")]
    public async Task Save_AStaleScreen_Returns409CommonE004()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request(["B520"]) with { UpdatedAt = Created.AddSeconds(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    private static SaveAlternateItemsRequest Request(
        string?[]? added = null, UpdatedAlternateItemRow[]? updated = null, long[]? deleted = null, bool moveConfirmed = false) =>
        new(BoltId, added, updated, deleted, moveConfirmed, Created);
}

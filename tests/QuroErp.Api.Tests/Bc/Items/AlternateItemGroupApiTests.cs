using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Items;
using static QuroErp.Api.Tests.Bc.Items.InMemoryAlternateItemGroupRepository;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>/api/bc/alternate-item-groups (BC-UC-019, MM-BR-810, MM-BR-813, MM-BR-814). Saves use their own factory.</summary>
public class AlternateItemGroupApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Batch = AlternateItemGroupRoutes.Base + "/" + AlternateItemGroupRoutes.Batch;

    [Fact, Trait("Id", "BC-UC-019-T03")]
    public async Task Search_ByItemClassOrKeyword_ForAnySignedInCaller_ReturnsGroupsInCodeOrder()
    {
        var client = factory.NonAdmin();

        var part = await client.GetFromJsonAsync<List<AlternateItemGroupRowDto>>(AlternateItemGroupRoutes.Base + "?itemClass=PART");
        var gear = await client.GetFromJsonAsync<List<AlternateItemGroupRowDto>>(AlternateItemGroupRoutes.Base + "?keyword=gea");

        part!.Select(g => (g.AltItemGroupCd, g.ItemCount)).Should().Equal(("BOLT", 2L), ("NUT", 0L));
        gear!.Select(g => (g.AltItemGroupCd, g.ItemClass)).Should().Equal(("GEAR", "ZZBC1"));
    }

    [Fact, Trait("Id", "BC-UC-019-T03")]
    public async Task Search_AnUnknownItemClass_Returns422CommonE002_AndWithoutToken_401()
    {
        using var unknown = await factory.NonAdmin().GetAsync(AlternateItemGroupRoutes.Base + "?itemClass=NOPE");
        using var anonymous = await factory.CreateClient().GetAsync(AlternateItemGroupRoutes.Base);

        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await unknown.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-002");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UC-019")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(Batch, Request([new("SPRING", "Springs")]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UC-019-T01"), Trait("Rule", "MM-BR-810")]
    public async Task Save_AddChangeAndDelete_Returns200WithTheGroupsOfTheItemClass()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(
            Batch, Request([new(" spring ", "Springs")], [new(BoltId, "BOLT-M6", "M6 bolts", Created)], [new(NutId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<AlternateItemGroupRowDto>>();
        rows!.Select(g => (g.AltItemGroupCd, g.AltItemGroupName)).Should().Equal(("BOLT-M6", "M6 bolts"), ("SPRING", "Springs"));
    }

    [Fact, Trait("Id", "BC-UC-019-T02"), Trait("Rule", "MM-BR-810"), Trait("Rule", "MM-BR-813")]
    public async Task Save_ATakenCode_Returns422MmE810_AndSavesNothing()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request([new("SPRING", "Springs"), new("gear", "Gears again")]));
        var after = await client.GetFromJsonAsync<List<AlternateItemGroupRowDto>>(AlternateItemGroupRoutes.Base + "?itemClass=PART");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-810");
        problem.GetProperty("args")[0].GetString().Should().Be("GEAR");
        after!.Select(g => g.AltItemGroupCd).Should().Equal("BOLT", "NUT");
    }

    [Fact, Trait("Id", "MM-UT-814"), Trait("Rule", "MM-BR-814")]
    public async Task Save_DeletingAGroupWithItems_Returns409MmE814()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request(deleted: [new(BoltId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-814");
        problem.GetProperty("args")[0].GetString().Should().Be("BOLT");
    }

    [Fact, Trait("Id", "BC-UC-019-T02")]
    public async Task Save_AStaleRow_Returns409CommonE004()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(Batch, Request(updated: [new(NutId, "NUT", "Nuts", Created.AddSeconds(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    private static SaveAlternateItemGroupsRequest Request(
        AlternateItemGroupInsert[]? added = null, AlternateItemGroupUpdate[]? updated = null, AlternateItemGroupDelete[]? deleted = null) =>
        new("PART", added, updated, deleted);
}

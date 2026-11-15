using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.Pallets;

namespace QuroErp.Api.Tests.Mm.Pallets;

/// <summary>Pallet management API (MM-UC-035, TASK-MM-053) on the in-memory Pallets of <see cref="InMemoryPalletRepository"/>.</summary>
public class PalletApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Stored = InMemoryPalletRepository.Stored;

    [Fact]
    public async Task Search_ForAnySignedInCaller_FiltersByEveryCondition_InNumberOrder()
    {
        var client = factory.NonAdmin();

        var lotL1 = await client.GetFromJsonAsync<List<PalletDto>>($"{PalletRoutes.Base}?itemId=10&lotNo=l1&status=active&warehouseId=1&locationId=2");
        var byNo = await client.GetFromJsonAsync<List<PalletDto>>($"{PalletRoutes.Base}?palletNo=pl2611000003");
        using var anonymous = await factory.CreateClient().GetAsync(PalletRoutes.Base);

        lotL1!.Select(p => p.PalletNo).Should().Contain("PL2611000001").And.NotContain("PL2611000003", "PL2611000004");
        lotL1!.Should().OnlyContain(p => p.Status == "ACTIVE" && p.LotNo == "L1");
        byNo.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 3L,
            ItemCd = "BOLT",
            LotNo = "L2",
            WarehouseCode = "MAIN",
            LocationCode = "A-01",
            Qty = 3m,
            Status = "ACTIVE",
            Events = (object?)null,
        });
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Rule", "MM-BR-916")]
    public async Task Get_ReturnsThePalletWithItsHistory_And404WhenMissing()
    {
        var client = factory.NonAdmin();

        var pallet = await client.GetFromJsonAsync<PalletDto>(PalletRoutes.Base + "/3");
        using var missing = await client.GetAsync(PalletRoutes.Base + "/999");

        pallet!.Events.Should().Equal(new PalletEventDto("CREATE", new DateOnly(2026, 11, 1), 3m, null, "GR2611000001"));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-006");
    }

    [Fact]
    public async Task ByNo_FindsAScannedNumber_IgnoringCase_And404WhenMissing()
    {
        var client = factory.NonAdmin();

        var pallet = await client.GetFromJsonAsync<PalletDto>(PalletRoutes.Base + "/by-no/pl2611000003");
        using var missing = await client.GetAsync(PalletRoutes.Base + "/by-no/PL0000000000");

        pallet!.Id.Should().Be(3);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Id", "MM-UC-035-T01"), Trait("Id", "MM-UC-035-T04"), Trait("Rule", "MM-BR-914")]
    public async Task Split_Returns201WithTheNewPallet_OnTheSameKey_AndBothHaveASplitEvent()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(PalletRoutes.Base + "/1/split", new SplitPalletRequest(2m, Stored));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<SplitPalletResultDto>())!;
        created.PalletNo.Should().MatchRegex(@"^PL\d{4}\d{6}$");
        var source = await client.GetFromJsonAsync<PalletDto>(PalletRoutes.Base + "/1");
        var split = await client.GetFromJsonAsync<PalletDto>(response.Headers.Location!.ToString());
        (source!.Qty, split!.Qty, split.ParentPalletNo, split.LotNo, split.LocationCode).Should().Be((4m, 2m, "PL2611000001", "L1", "A-01"));
        source.Events!.Select(e => (e.EventType, e.Qty, e.RelatedPalletNo)).Should().EndWith(("SPLIT", -2m, created.PalletNo));
        split.Events!.Select(e => (e.EventType, e.Qty, e.RelatedPalletNo)).Should().Equal(("SPLIT", 2m, "PL2611000001"));
    }

    [Theory, Trait("Id", "MM-UT-914"), Trait("Rule", "MM-BR-914")]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    [InlineData(4, 1)]
    public async Task Split_NothingOrTheWholeQty_OrAClosedPallet_Returns422MmE914(long id, decimal qty)
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync($"{PalletRoutes.Base}/{id}/split", new SplitPalletRequest(qty, Stored));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-914");
    }

    [Fact]
    public async Task Split_AStaleRead_Returns409()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(PalletRoutes.Base + "/2/split", new SplitPalletRequest(1m, Stored.AddDays(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, Trait("Id", "MM-UC-035-T03"), Trait("Rule", "MM-BR-915")]
    public async Task Merge_AnotherLot_Returns422MmE915_AndAMissingPallet404()
    {
        var client = await factory.AdminAsync();

        using var otherLot = await client.PostAsJsonAsync(PalletRoutes.Merge, new MergePalletsRequest(2, [3]));
        using var missing = await client.PostAsJsonAsync(PalletRoutes.Merge, new MergePalletsRequest(2, [999]));

        otherLot.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await otherLot.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-915");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Id", "MM-UC-035-T02"), Trait("Rule", "MM-BR-915")]
    public async Task Merge_ClosesTheSource_AndReturnsTheTargetWithTheTotal()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(PalletRoutes.Merge, new MergePalletsRequest(5, [2]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var target = (await response.Content.ReadFromJsonAsync<PalletDto>())!;
        (target.Qty, target.Events![^1]).Should().Be((9m, new PalletEventDto("MERGE", target.Events[^1].EventDt, 4m, "PL2611000002", null)));
        var source = await client.GetFromJsonAsync<PalletDto>(PalletRoutes.Base + "/2");
        (source!.Qty, source.Status).Should().Be((0m, "CLOSED"));
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public async Task Changes_WithoutFullAccess_Return403_AndWithoutSignIn_Return401()
    {
        using var split = await factory.NonAdmin().PostAsJsonAsync(PalletRoutes.Base + "/1/split", new SplitPalletRequest(1m, Stored));
        using var merge = await factory.NonAdmin().PostAsJsonAsync(PalletRoutes.Merge, new MergePalletsRequest(1, [5]));
        using var anonymous = await factory.CreateClient().PostAsJsonAsync(PalletRoutes.Merge, new MergePalletsRequest(1, [5]));

        split.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        merge.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

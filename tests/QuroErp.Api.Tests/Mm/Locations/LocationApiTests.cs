using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;

namespace QuroErp.Api.Tests.Mm.Locations;

/// <summary>Location master API (MM-UC-034, TASK-MM-051) on the in-memory Locations of <see cref="InMemoryLocationRepository"/>.</summary>
public class LocationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Stored = InMemoryLocationRepository.Stored;

    [Fact, Trait("Id", "MM-UC-034-T01")]
    public async Task Search_ForAnySignedInCaller_ReturnsTheWarehouseLocations_StarFirst()
    {
        var client = factory.NonAdmin();

        var inUse = await client.GetFromJsonAsync<List<LocationDto>>($"{LocationRoutes.Base}?warehouseId=1");
        var all = await client.GetFromJsonAsync<List<LocationDto>>($"{LocationRoutes.Base}?warehouseId=1&includeInactive=true");

        inUse!.Select(l => l.LocationCode).Should().Equal("*", "A-01");
        inUse![0].Should().Be(new LocationDto(1, 1, "*", null, true, true, null, 0, Stored));
        all!.Select(l => (l.LocationCode, l.UseYn)).Should().Equal(("*", true), ("A-01", true), ("B-01", false));
    }

    [Fact]
    public async Task Search_WithoutAWarehouse_Returns422CommonE005()
    {
        using var response = await factory.NonAdmin().GetAsync(LocationRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-005");
    }

    [Fact, Trait("Id", "MM-UC-034-T06"), Trait("Rule", "MM-BR-717")]
    public async Task Options_ForAnySignedInCaller_ListLocationsInUse_StarFirst_AndNeedSignIn()
    {
        var options = await factory.NonAdmin().GetFromJsonAsync<List<LocationOptionDto>>($"{LocationRoutes.Options}?warehouseId=1");
        using var anonymous = await factory.CreateClient().GetAsync($"{LocationRoutes.Options}?warehouseId=1");

        options.Should().Equal(new LocationOptionDto(1, "*", null), new LocationOptionDto(2, "A-01", "Shelf A1"));
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "MM-UC-034-T02")]
    public async Task Save_AsAdmin_AddsARowInUpperCase_AndReturnsTheWarehouseLocations()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(LocationRoutes.Batch, Batch(added: [NewRow(2, " c-09 ")]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<LocationDto>>())!.Select(l => (l.WarehouseId, l.LocationCode))
            .Should().Equal((2L, "*"), (2L, "C-09"));
    }

    [Theory, Trait("Id", "MM-UC-034-T03"), Trait("Id", "MM-UC-034-T04"), Trait("Rule", "MM-BR-712")]
    [InlineData("BAD CODE")]
    [InlineData("a-01")]
    public async Task Save_WithAnInvalidOrRepeatedCode_Returns422MmE712(string code)
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(LocationRoutes.Batch, Batch(added: [NewRow(1, code)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-712");
    }

    [Fact, Trait("Id", "MM-UC-034-T04"), Trait("Rule", "MM-BR-712")]
    public async Task Save_DeletingTheSystemLocation_Returns422MmE712()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(LocationRoutes.Batch, Batch(deleted: [new LocationKey(1, Stored)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-712");
    }

    [Fact, Trait("Id", "MM-UC-034-T05"), Trait("Rule", "MM-BR-717")]
    public async Task Save_SettingALocationWithABalanceNotInUse_Returns422MmE717()
    {
        var client = await factory.AdminAsync();
        var row = new LocationSaveRow(2, 1, "A-01", "Shelf A1", false, null, Stored);

        using var response = await client.PostAsJsonAsync(LocationRoutes.Batch, Batch(updated: [row]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-717");
    }

    [Fact]
    public async Task Save_WithARowChangedAfterItWasRead_Returns409()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(LocationRoutes.Batch, Batch(deleted: [new LocationKey(3, Stored.AddDays(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public async Task Save_WithoutFullAccess_Returns403_AndWithoutSignIn_Returns401()
    {
        using var nonAdmin = await factory.NonAdmin().PostAsJsonAsync(LocationRoutes.Batch, Batch());
        using var anonymous = await factory.CreateClient().PostAsJsonAsync(LocationRoutes.Batch, Batch());

        nonAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static LocationSaveRow NewRow(long warehouseId, string code) => new(null, warehouseId, code, null, null, null, null);

    private static BatchRequest<LocationSaveRow, LocationKey> Batch(
        IReadOnlyList<LocationSaveRow>? added = null, IReadOnlyList<LocationSaveRow>? updated = null, IReadOnlyList<LocationKey>? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);
}

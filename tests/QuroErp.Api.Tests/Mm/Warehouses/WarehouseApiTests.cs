using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Api.Tests.Mm.Warehouses;

/// <summary>Warehouse master API (MM-UC-017, TASK-MM-025) and GET /api/mm/warehouses/options (TASK-CORE-022, TASK-CORE-029).</summary>
public class WarehouseApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Stored = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact, Trait("Id", "CORE-022-T01")]
    public async Task Options_ForAnySignedInCaller_ReturnWarehousesInUse_WithoutTheConsignmentWarehouse()
    {
        var options = await factory.NonAdmin().GetFromJsonAsync<List<WarehouseOptionDto>>(WarehouseRoutes.Options);

        options.Should().Equal(
            new WarehouseOptionDto(1, "MAIN", "Main", "NORMAL", true), new WarehouseOptionDto(2, "W02", "Second", "NORMAL", true));
    }

    [Fact, Trait("Id", "MM-UT-710"), Trait("Rule", "MM-BR-710")]
    public async Task Options_ForReportConditions_IncludeTheConsignmentWarehouse_WithItsType()
    {
        var options = await factory.NonAdmin().GetFromJsonAsync<List<WarehouseOptionDto>>($"{WarehouseRoutes.Options}?includeConsignment=true");

        options!.Select(o => (o.WarehouseCode, o.WarehouseType)).Should().Equal(
            ("MAIN", "NORMAL"), ("W02", "NORMAL"), ("CONSIGN", "CONSIGNMENT"));
    }

    [Fact, Trait("Id", "MM-UC-017-T10")]
    public async Task Options_WithIncludeInactive_AddWarehousesNotInUse()
    {
        var options = await factory.NonAdmin().GetFromJsonAsync<List<WarehouseOptionDto>>($"{WarehouseRoutes.Options}?includeInactive=true");

        options!.Select(o => (o.WarehouseCode, o.UseYn)).Should().Equal(("MAIN", true), ("W02", true), ("OLD", false));
    }

    [Fact, Trait("Id", "CORE-022-T02")]
    public async Task Options_WithoutSignIn_Return401()
    {
        using var response = await factory.CreateClient().GetAsync(WarehouseRoutes.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "MM-UC-017-T01")]
    public async Task Search_ReturnsWarehousesInUse_AndWithIncludeInactiveAlsoTheOthers()
    {
        var client = factory.NonAdmin();

        var inUse = await client.GetFromJsonAsync<List<WarehouseDto>>(WarehouseRoutes.Base);
        var all = await client.GetFromJsonAsync<List<WarehouseDto>>($"{WarehouseRoutes.Base}?includeInactive=true");

        inUse!.Select(w => w.WarehouseCode).Should().Equal("MAIN", "CONSIGN");
        inUse![0].Should().Be(new WarehouseDto(1, "MAIN", "Main", true, true, "NORMAL", null, Stored));
        all!.Select(w => w.WarehouseCode).Should().Equal("MAIN", "CONSIGN", "OLD");
    }

    [Fact, Trait("Id", "MM-UC-017-T01")]
    public async Task Save_AsAdmin_ReturnsEveryWarehouse()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(WarehouseRoutes.Batch, Batch());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<WarehouseDto>>())!.Select(w => w.WarehouseCode).Should().Equal("MAIN", "CONSIGN", "OLD");
    }

    [Fact, Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public async Task Save_WithAnInvalidCode_Returns422MmE701()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(WarehouseRoutes.Batch, Batch(added: [NewRow("BAD CODE")]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-701");
    }

    [Fact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public async Task Save_AddingAConsignmentWarehouse_Returns422MmE709()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(WarehouseRoutes.Batch, Batch(added: [NewRow("CON2") with { WarehouseType = "CONSIGNMENT" }]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-709");
    }

    [Fact]
    public async Task Save_WithARowChangedAfterItWasRead_Returns409()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(WarehouseRoutes.Batch, Batch(deleted: [new WarehouseKey(4, Stored.AddDays(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public async Task Save_WithoutFullAccess_Returns403_AndWithoutSignIn_Returns401()
    {
        using var nonAdmin = await factory.NonAdmin().PostAsJsonAsync(WarehouseRoutes.Batch, Batch());
        using var anonymous = await factory.CreateClient().PostAsJsonAsync(WarehouseRoutes.Batch, Batch());

        nonAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static WarehouseSaveRow NewRow(string code) => new(null, code, "New", false, true, null, null, null);

    private static BatchRequest<WarehouseSaveRow, WarehouseKey> Batch(
        IReadOnlyList<WarehouseSaveRow>? added = null, IReadOnlyList<WarehouseKey>? deleted = null) =>
        new(added ?? [], [], deleted ?? []);
}

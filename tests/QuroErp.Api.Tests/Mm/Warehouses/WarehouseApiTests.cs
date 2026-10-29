using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Api.Tests.Mm.Warehouses;

/// <summary>GET /api/mm/warehouses/options (TASK-CORE-022).</summary>
public class WarehouseApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "CORE-022-T01")]
    public async Task Options_ForAnySignedInCaller_ReturnIdCodeAndName()
    {
        var options = await factory.NonAdmin().GetFromJsonAsync<List<WarehouseOptionDto>>(WarehouseRoutes.Options);

        options.Should().Equal(new WarehouseOptionDto(1, "MAIN", "Main"), new WarehouseOptionDto(2, "W02", "Second"));
    }

    [Fact, Trait("Id", "CORE-022-T02")]
    public async Task Options_WithoutSignIn_Return401()
    {
        using var response = await factory.CreateClient().GetAsync(WarehouseRoutes.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

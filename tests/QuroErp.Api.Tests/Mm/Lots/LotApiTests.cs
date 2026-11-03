using System.Net;
using FluentAssertions;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// GET /api/mm/lots (TASK-CORE-027, MM-BR-362). Balances, date and order are covered by LotServiceTests on real PostgreSQL;
/// these check sign-in and the required conditions.
/// </summary>
public class LotApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "MM-UT-362")]
    public async Task Search_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{LotRoutes.Base}?itemId=1&warehouseId=1&date=2026-10-10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "MM-UT-362"), Trait("Rule", "MM-BR-362")]
    [InlineData("warehouseId=1&date=2026-10-10", "itemId")]
    [InlineData("itemId=1&date=2026-10-10", "warehouseId")]
    [InlineData("itemId=1&warehouseId=1", "date")]
    public async Task Search_WithoutAnItemWarehouseOrDate_Returns422(string query, string missing)
    {
        using var response = await factory.NonAdmin().GetAsync($"{LotRoutes.Base}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("Common-E-005");
        problem.GetProperty("args")[0].GetString().Should().Be(missing);
    }
}

using System.Net;
using FluentAssertions;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// GET /api/mm/stock-suggestions (TASK-CORE-068, MM-BR-716). FIFO order and splitting are covered by StockSuggestionTests and
/// LotServiceTests on real PostgreSQL; these check sign-in and the required conditions.
/// </summary>
public class StockSuggestionApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "MM-UT-716")]
    public async Task Suggest_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{LotRoutes.Suggestions}?itemId=1&warehouseId=1&qty=1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    [InlineData("warehouseId=1&qty=1", "itemId")]
    [InlineData("itemId=1&qty=1", "warehouseId")]
    [InlineData("itemId=1&warehouseId=1", "qty")]
    [InlineData("itemId=1&warehouseId=1&qty=0", "qty")]
    [InlineData("itemId=1&warehouseId=1&qty=-2", "qty")]
    public async Task Suggest_WithoutAnItemWarehouseOrAQuantityAboveZero_Returns422(string query, string missing)
    {
        using var response = await factory.NonAdmin().GetAsync($"{LotRoutes.Suggestions}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("Common-E-005");
        problem.GetProperty("args")[0].GetString().Should().Be(missing);
    }
}

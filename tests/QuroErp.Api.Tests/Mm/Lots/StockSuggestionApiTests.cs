using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// GET /api/mm/stock-suggestions (TASK-CORE-068, MM-BR-716). FIFO order and splitting are covered by StockSuggestionTests and
/// LotServiceTests on real PostgreSQL; these check sign-in, the required conditions and the response shape with shortQty.
/// </summary>
public class StockSuggestionApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716")]
    [InlineData(5, new[] { 2.0, 3.0 }, 0)]
    [InlineData(8, new[] { 2.0, 4.0 }, 2)]
    public async Task Suggest_ReturnsFifoLines_AndTheShortQuantity(decimal qty, double[] lineQty, decimal shortQty)
    {
        using var response = await factory.NonAdmin().GetAsync(
            $"{LotRoutes.Suggestions}?itemId={InMemoryLotRepository.ItemId}&warehouseId={InMemoryLotRepository.WarehouseId}&qty={qty}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => (l.GetProperty("locationCode").GetString(), l.GetProperty("lotNo").GetString()))
            .Should().Equal(("*", "*"), ("A01", "260901-001"));
        lines.Select(l => l.GetProperty("qty").GetDecimal()).Should().Equal(lineQty.Select(q => (decimal)q));
        lines[1].GetProperty("locationId").GetInt64().Should().Be(10);
        lines[1].GetProperty("lotId").GetInt64().Should().Be(21);
        body.GetProperty("shortQty").GetDecimal().Should().Be(shortQty);
    }

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

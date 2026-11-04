using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Sd.SalesOrders;

namespace QuroErp.Api.Tests.Sd.SalesOrders;

/// <summary>
/// /api/sd/sales-orders access (SD-UC-002, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the SalesOrderServiceTests on real PostgreSQL; these check that the screen guards every route before anything is read.
/// </summary>
public class SalesOrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveSalesOrderRequest Order = new(
        1, new DateOnly(2026, 11, 5), null, null, "USD", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, [], null);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Routes_WithoutTheScreen_Return403()
    {
        var client = factory.NonAdmin();
        using var file = new MultipartFormDataContent { { new StringContent("BOLT,1"), SalesOrderRoutes.LineFileFormField, "lines.csv" } };

        var responses = new[]
        {
            await client.GetAsync(SalesOrderRoutes.Base),
            await client.PostAsJsonAsync(SalesOrderRoutes.Base, Order),
            await client.PutAsJsonAsync($"{SalesOrderRoutes.Base}/1", Order),
            await client.DeleteAsync($"{SalesOrderRoutes.Base}/1?updatedAt=2026-11-05T00:00:00Z"),
            await client.GetAsync($"{SalesOrderRoutes.Base}/{SalesOrderRoutes.LineDefaults}?customerId=1&itemCd=BOLT"),
            await client.PostAsync($"{SalesOrderRoutes.Base}/{SalesOrderRoutes.LineFile}?customerId=1&kind=ITEM", file),
            await client.GetAsync($"{SalesOrderRoutes.Base}/1/{SalesOrderRoutes.PrintSalesOrder}"),
            await client.GetAsync($"{SalesOrderRoutes.Base}/1/{SalesOrderRoutes.PrintPickingList}"),
            await client.GetAsync($"{SalesOrderRoutes.Base}/1/{SalesOrderRoutes.PrintSetComponentList}"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(SalesOrderRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

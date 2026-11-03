using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.PurchaseOrders;

namespace QuroErp.Api.Tests.Mm.PurchaseOrders;

/// <summary>
/// /api/mm/purchase-orders access (MM-UC-003, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the PurchaseOrderServiceTests on real PostgreSQL; these check that every change needs Full Access before anything is read.
/// </summary>
public class PurchaseOrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SavePurchaseOrderRequest Order = new(1, new DateOnly(2026, 10, 30), "USD", null, null, null, null, null, null, null, [], null);
    private static readonly PurchaseOrderActionRequest Action = new(DateTimeOffset.UnixEpoch);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(PurchaseOrderRoutes.Base, Order),
            await client.PutAsJsonAsync($"{PurchaseOrderRoutes.Base}/1", Order),
            await client.DeleteAsync($"{PurchaseOrderRoutes.Base}/1?updatedAt=2026-10-30T00:00:00Z"),
            await client.PostAsJsonAsync($"{PurchaseOrderRoutes.Base}/1/{PurchaseOrderRoutes.Approve}", Action),
            await client.PostAsJsonAsync($"{PurchaseOrderRoutes.Base}/1/{PurchaseOrderRoutes.Close}", Action),
            await client.PostAsJsonAsync($"{PurchaseOrderRoutes.Base}/1/{PurchaseOrderRoutes.Cancel}", Action),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(PurchaseOrderRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

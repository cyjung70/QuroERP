using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.GoodsReceipts;

namespace QuroErp.Api.Tests.Mm.GoodsReceipts;

/// <summary>
/// /api/mm/goods-receipts access (MM-UC-004, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the GoodsReceiptServiceTests on real PostgreSQL; these check that every change needs Full Access before anything is read.
/// </summary>
public class GoodsReceiptApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveGoodsReceiptRequest Receipt = new(1, new DateOnly(2026, 11, 4), null, null, null, null, null, [], null);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(GoodsReceiptRoutes.Base, Receipt),
            await client.PutAsJsonAsync($"{GoodsReceiptRoutes.Base}/1", Receipt),
            await client.DeleteAsync($"{GoodsReceiptRoutes.Base}/1?updatedAt=2026-11-04T00:00:00Z"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Reads_WithoutTheScreen_Return403_AndWithoutSignIn_Return401()
    {
        var client = factory.NonAdmin();

        (await client.GetAsync(GoodsReceiptRoutes.Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{GoodsReceiptRoutes.Base}/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{GoodsReceiptRoutes.Base}/{GoodsReceiptRoutes.OpenPoLines}?vendorId=1")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync(GoodsReceiptRoutes.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

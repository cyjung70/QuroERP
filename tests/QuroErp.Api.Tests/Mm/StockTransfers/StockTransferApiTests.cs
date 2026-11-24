using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.StockTransfers;

namespace QuroErp.Api.Tests.Mm.StockTransfers;

/// <summary>
/// /api/mm/stock-transfers access (MM-UC-018, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and the
/// StockTransferServiceTests on real PostgreSQL; these check that the screen guards every change before anything is read.
/// </summary>
public class StockTransferApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveStockTransferRequest Request =
        new("TR", null, 1, 2, null, [new SaveStockTransferLineRequest(1, 1m)], DateTimeOffset.UtcNow);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutTheScreen_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(StockTransferRoutes.Base, Request),
            await client.PutAsJsonAsync("api/mm/stock-transfers/1", Request),
            await client.DeleteAsync("api/mm/stock-transfers/1?updatedAt=2026-11-10T00:00:00Z"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reads_WithoutSignIn_Return401()
    {
        var client = factory.CreateClient();

        var responses = new[]
        {
            await client.GetAsync(StockTransferRoutes.Base),
            await client.GetAsync("api/mm/stock-transfers/1"),
            await client.GetAsync(StockTransferRoutes.ScanPlace + "?value=LC|MAIN|A1&step=FROM"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
    }
}

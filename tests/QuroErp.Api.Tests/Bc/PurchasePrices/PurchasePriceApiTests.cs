using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Tests.Bc.PurchasePrices;

/// <summary>
/// /api/bc/purchase-prices access (BC-UC-015, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the PurchasePriceServiceTests on real PostgreSQL; these check that a save needs Full Access before anything is read.
/// </summary>
public class PurchasePriceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly BatchRequest<PurchasePriceRowRequest, PurchasePriceDeleteRequest> Batch =
        new([new PurchasePriceRowRequest(null, "A1", 10m, null)], [], []);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Batch_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync($"{PurchasePriceRoutes.Base}/{PurchasePriceRoutes.Batch}?vendorPrice=VP1", Batch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{PurchasePriceRoutes.Base}?vendorPrice=VP1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

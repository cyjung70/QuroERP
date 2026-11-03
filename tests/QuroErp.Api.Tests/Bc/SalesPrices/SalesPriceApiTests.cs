using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Tests.Bc.SalesPrices;

/// <summary>
/// /api/bc/sales-prices access (BC-UC-014, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the SalesPriceServiceTests on real PostgreSQL; these check that a save needs Full Access before anything is read.
/// </summary>
public class SalesPriceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> Batch =
        new([new SalesPriceRowRequest(null, "A1", 10m, null)], [], []);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Batch_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync($"{SalesPriceRoutes.Base}/{SalesPriceRoutes.Batch}?priceLevel=PL1", Batch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{SalesPriceRoutes.Base}?priceLevel=PL1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "SD-UT-409"), Trait("Rule", "SD-BR-409")]
    [InlineData("ABCDEF", "SD-E-403")]
    [InlineData(" ", "Common-E-005")]
    public async Task List_ALevelOver5CharactersOrNone_Returns422(string level, string code)
    {
        using var response = await factory.NonAdmin().GetAsync($"{SalesPriceRoutes.Base}?priceLevel={Uri.EscapeDataString(level)}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(code);
    }
}

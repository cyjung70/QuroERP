using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Tests.Bc.SetItems;

/// <summary>
/// /api/bc/set-items access (BC-UC-013, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// SetItemServiceTests on real PostgreSQL; these check sign-in, Full Access and the required set code.
/// </summary>
public class SetItemApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> Batch =
        new([new SetComponentRowRequest(null, "A1", 1m, null)], [], []);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Batch_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync($"{SetItemRoutes.Base}/{SetItemRoutes.Batch}?itemCd=KIT", Batch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{SetItemRoutes.Base}?itemCd=KIT");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "SD-UT-501")]
    public async Task Get_WithoutASetCode_Returns422()
    {
        using var response = await factory.NonAdmin().GetAsync($"{SetItemRoutes.Base}?itemCd=%20");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-005");
    }
}

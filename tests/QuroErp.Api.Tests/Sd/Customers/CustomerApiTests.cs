using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;

namespace QuroErp.Api.Tests.Sd.Customers;

/// <summary>
/// /api/sd/customers access (SD-UC-001, SD-BR-306, BC-BR-214). The rules and the database behaviour are covered by the Domain
/// tests and CustomerServiceTests on real PostgreSQL; these check that every change needs Full Access before anything is read.
/// </summary>
public class CustomerApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey> NoRows = new([], [], []);

    [Fact, Trait("Id", "SD-UT-306"), Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(CustomerRoutes.Base, new CreateCustomerRequest { CName = "ACME", Terms = "NET_30" }),
            await client.PutAsJsonAsync($"{CustomerRoutes.Base}/1", new UpdateCustomerRequest { Terms = "NET_30", FreightCharge = "N" }),
            await client.DeleteAsync($"{CustomerRoutes.Base}/1?updatedAt=2026-11-04T00:00:00Z"),
            await client.PutAsJsonAsync($"{CustomerRoutes.Base}/1/{CustomerRoutes.TaxExemptions}", NoRows),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Reads_WithoutSignIn_Return401()
    {
        var client = factory.CreateClient();

        (await client.GetAsync(CustomerRoutes.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{CustomerRoutes.Base}/1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{CustomerRoutes.Base}/1/{CustomerRoutes.TaxExemptions}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

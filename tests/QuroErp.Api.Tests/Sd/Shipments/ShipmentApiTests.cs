using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Sd.Shipments;

namespace QuroErp.Api.Tests.Sd.Shipments;

/// <summary>
/// /api/sd/shipments access (SD-UC-051, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the ShipmentServiceTests on real PostgreSQL; these check that the screen guards every route before anything is read.
/// </summary>
public class ShipmentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveShipmentRequest Shipment = new(
        1, new DateOnly(2026, 11, 5), null, null, null, null, null, null, null, null, null, null, null, null, null, [], null);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Routes_WithoutTheScreen_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.GetAsync(ShipmentRoutes.Base),
            await client.GetAsync($"{ShipmentRoutes.Base}/1"),
            await client.PostAsJsonAsync(ShipmentRoutes.Base, Shipment),
            await client.PutAsJsonAsync($"{ShipmentRoutes.Base}/1", Shipment),
            await client.PostAsJsonAsync($"{ShipmentRoutes.Base}/1/{ShipmentRoutes.Void}", new VoidShipmentRequest(DateTimeOffset.UtcNow)),
            await client.GetAsync($"{ShipmentRoutes.Base}/{ShipmentRoutes.OpenOrderLines}?customerId=1"),
            await client.GetAsync($"{ShipmentRoutes.Base}/{ShipmentRoutes.UnbilledLines}?customerId=1&currencyCode=USD"),
            await client.GetAsync($"{ShipmentRoutes.Base}/{ShipmentRoutes.UnshippedInvoiceLines}?customerId=1"),
            await client.GetAsync($"{ShipmentRoutes.Base}/{ShipmentRoutes.Pallets}?warehouseId=1"),
            await client.GetAsync($"{ShipmentRoutes.Base}/1/{ShipmentRoutes.Print}"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(ShipmentRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "BC-UT-214"), Trait("Id", "MM-UT-921")]
    [InlineData(ShipmentRoutes.OpenOrderLines + "?soNo=SO-1&itemId=1")]
    [InlineData(ShipmentRoutes.UnshippedInvoiceLines + "?dnNo=DN-1&itemId=1")]
    public async Task PdaScanLookup_WithoutTheScreen_Returns403_AndWithoutSignIn_Returns401(string route)
    {
        var path = $"{ShipmentRoutes.Base}/{route}";

        (await factory.NonAdmin().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "SD-UC-054-T04"), Trait("Id", "MM-UT-921")]
    [InlineData(ShipmentRoutes.OpenOrderLines)]
    [InlineData(ShipmentRoutes.OpenOrderLines + "?soNo=%20&itemId=1")]
    [InlineData(ShipmentRoutes.UnshippedInvoiceLines)]
    [InlineData(ShipmentRoutes.UnshippedInvoiceLines + "?dnNo=%20")]
    public async Task OpenLines_WithoutCustomerOrNumber_Returns400(string route)
    {
        var client = await factory.AdminAsync();

        using var response = await client.GetAsync($"{ShipmentRoutes.Base}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-005");
    }
}

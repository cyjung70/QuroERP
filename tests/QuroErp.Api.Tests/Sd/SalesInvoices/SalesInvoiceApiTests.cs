using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Sd.SalesInvoices;

namespace QuroErp.Api.Tests.Sd.SalesInvoices;

/// <summary>
/// /api/sd/sales-invoices access (SD-UC-003, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and
/// the SalesInvoiceServiceTests on real PostgreSQL; these check that the screen guards every route before anything is read.
/// </summary>
public class SalesInvoiceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveSalesInvoiceRequest Invoice = new(
        1, new DateOnly(2026, 11, 5), null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        [], null);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Routes_WithoutTheScreen_Return403()
    {
        var client = factory.NonAdmin();
        var at = DateTimeOffset.UtcNow;

        var responses = new[]
        {
            await client.GetAsync(SalesInvoiceRoutes.Base),
            await client.GetAsync($"{SalesInvoiceRoutes.Base}/1"),
            await client.PostAsJsonAsync(SalesInvoiceRoutes.Base, Invoice),
            await client.PutAsJsonAsync($"{SalesInvoiceRoutes.Base}/1", Invoice),
            await client.DeleteAsync($"{SalesInvoiceRoutes.Base}/1?updatedAt={Uri.EscapeDataString(at.ToString("O"))}"),
            await client.PostAsJsonAsync($"{SalesInvoiceRoutes.Base}/1/{SalesInvoiceRoutes.Void}", new VoidSalesInvoiceRequest(at)),
            await client.GetAsync($"{SalesInvoiceRoutes.Base}/{SalesInvoiceRoutes.OpenOrderLines}?customerId=1&currencyCode=USD"),
            await client.PostAsJsonAsync(
                $"{SalesInvoiceRoutes.Base}/1/{SalesInvoiceRoutes.CancelUnshipped}",
                new CancelUnshippedRequest(new DateOnly(2026, 11, 5), null, [new(1, 1m)], at)),
            await client.DeleteAsync($"{SalesInvoiceRoutes.Base}/1/{SalesInvoiceRoutes.Cancellations}/2"),
            await client.GetAsync($"{SalesInvoiceRoutes.Base}/1/{SalesInvoiceRoutes.PrintInvoice}"),
            await client.GetAsync($"{SalesInvoiceRoutes.Base}/1/{SalesInvoiceRoutes.PrintTaxInvoice}"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(SalesInvoiceRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

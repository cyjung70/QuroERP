using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.SupplierInvoices;

namespace QuroErp.Api.Tests.Mm.SupplierInvoices;

/// <summary>
/// /api/mm/supplier-invoices access (MM-UC-032, BC-BR-214). The rules and the database behaviour are covered by the Domain tests
/// and the SupplierInvoiceServiceTests on real PostgreSQL; these check that every change needs Full Access before anything is read.
/// </summary>
public class SupplierInvoiceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly SaveSupplierInvoiceRequest Invoice = new(1, "INV-1", new DateOnly(2026, 11, 20), null, null, null, false, null, [], null);

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(SupplierInvoiceRoutes.Base, Invoice),
            await client.PutAsJsonAsync($"{SupplierInvoiceRoutes.Base}/1", Invoice),
            await client.DeleteAsync($"{SupplierInvoiceRoutes.Base}/1?updatedAt=2026-11-04T00:00:00Z"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Reads_WithoutTheScreen_Return403_AndWithoutSignIn_Return401()
    {
        var client = factory.NonAdmin();

        (await client.GetAsync(SupplierInvoiceRoutes.Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{SupplierInvoiceRoutes.Base}/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{SupplierInvoiceRoutes.Base}/{SupplierInvoiceRoutes.OpenReceiptLines}?vendorId=1")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync(SupplierInvoiceRoutes.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Sd.SalesInvoices;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Infrastructure.Reports;
using QuroErp.Infrastructure.Reports.Sd;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>Sales invoice and KR tax invoice (print-documents.md, SD-BR-134) as PDF with real Chromium.</summary>
public class SalesInvoiceDocumentTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private static readonly PrintCompany Company = new("Quro Trading", ["1 Market St"], "555-0000", "office@example.com");

    private static readonly SalesInvoiceDto Invoice = new(
        701, "DN0000042", "PA", "SHIP_THEN_BILL", 801, "Blue River Market", null, "SO2611000001", new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 5),
        1, "USD", 1m, "DEMO_A", "NET_30", "UPS", null, "Blue River Dock", "2 Pier Rd", null, "Dallas", "TX", "75001", "USA", "555-0102", null,
        "Thank you", null, 1030m, 90m, 1120m, 1120m, 400m, 400m, 0m, 0m, null, null,
        [
            new SalesInvoiceLineDto(
                9202, 2, 12, "FRT", "Freight", "EA", 1, "MAIN", null, null, 1m, 130m, 130m, null, 0m, null, 2, 130m, 0m, null, null, null, null, null,
                null, 0m, 0m, 0m),
            new SalesInvoiceLineDto(
                9201, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 10m, 100m, 900m, "A", -10m, 100m, 1, 900m, 90m, null, null, null, 31,
                "SH2611000001", 1, 0m, 0m, 0m),
        ],
        [], DateTimeOffset.UnixEpoch);

    private static SalesInvoiceDocument Document(PdfRenderer renderer) =>
        new(renderer, new PrintTemplateResolver(Path.GetTempPath()), NullLogger<SalesInvoiceDocument>.Instance);

    [Fact, Trait("Id", "SD-UC-003-T06")]
    public async Task SalesInvoice_HasPartiesLinesInOrderDiscountTotalsAndBalance()
    {
        var print = new SalesInvoicePrint(
            Company, new SalesOrderPrintParty("Blue River Market", ["9 Lake Rd"], null),
            new SalesOrderPrintParty("Blue River Dock", ["2 Pier Rd"], "555-0102"), Invoice, 2, 2, 2, ShowDiscount: true);

        var text = Text(await Document(fixture.Renderer).SalesInvoicePdfAsync(print, PrintPaper.Letter, new CultureInfo("en"), CancellationToken.None));

        text.Should().Contain("Invoice").And.Contain("DN0000042").And.Contain("BlueRiverMarket").And.Contain("BlueRiverDock")
            .And.Contain("-10.00").And.Contain("1,120.00").And.Contain("BalanceDue").And.Contain("720.00").And.Contain("Thankyou");
        text.IndexOf("HexBolt", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Freight", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "SD-UT-134")]
    public async Task TaxInvoice_HasSupplierBuyerAndLineSupplyAndTax()
    {
        var print = new TaxInvoicePrint(
            Company, new TaxInvoiceParty("111-22-33333", "Quro Trading", "Demo CEO", "1 Market St", "Wholesale", "Hardware"),
            new TaxInvoiceParty("444-55-66666", "Blue River Market", "Demo Buyer", "9 Lake Rd", "Retail", "Tools"), Invoice, 0, 0);

        var text = Text(await Document(fixture.Renderer).TaxInvoicePdfAsync(print, PrintPaper.A4, new CultureInfo("ko"), CancellationToken.None));

        text.Should().Contain("세금계산서").And.Contain("공급자").And.Contain("공급받는자").And.Contain("111-22-33333").And.Contain("444-55-66666")
            .And.Contain("공급가액").And.Contain("1,030").And.Contain("1,120");
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(string.Concat(document.GetPages().Select(p => p.Text)).Where(c => !char.IsWhiteSpace(c)));
    }
}

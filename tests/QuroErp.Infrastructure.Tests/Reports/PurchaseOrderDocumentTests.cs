using System.Globalization;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Infrastructure.Reports;
using QuroErp.Infrastructure.Reports.Mm;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>The purchase order form (print-documents.md "purchase-order", MM-BR-021) as PDF with real Chromium and as XLSX.</summary>
public class PurchaseOrderDocumentTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private static readonly PurchaseOrderPrint Print = new(
        new PrintCompany("큐로 상사", ["서울시 중구 세종대로 1"], "02-000-0000", "office@example.com"),
        new PurchaseOrderPrintVendor("ACME", ["1 Main St", "Springfield CA 90001", "USA"], "Kim", "555-0100", "555-0199", "orders@example.com"),
        new PurchaseOrderDto(
            501, "PO2611000001", "PO", 701, "ACME", new DateOnly(2026, 11, 4), "USD", 1382.5m, "FOB", new DateOnly(2026, 11, 30), 1,
            "NET 30", "KIM", "Deliver to dock 2", 1402.50m, 70.13m, 1332.37m, 133.24m, 1465.61m, 2026206m, 1,
            null, null, null, null, null, null, 0m,
            [
                new PurchaseOrderLineDto(9002, 2, 12, "PANEL", "Steel Panel", 1m, 0m, 0m, 0m, 0m, 0m, 1, 0m, 0m, null, 1, 0m, "EA"),
                new PurchaseOrderLineDto(9001, 1, 11, "BOLT", "Hex Bolt", 5m, 280.5m, 1402.50m, 5m, 70.13m, 1332.37m, 1, 1332.37m, 133.24m,
                    new DateOnly(2026, 11, 30), 1, 0m, "EA"),
            ],
            DateTimeOffset.UnixEpoch),
        2, 4, 2);

    private static PurchaseOrderDocument Document(PdfRenderer renderer) =>
        new(renderer, new ExcelWriter(), new PrintTemplateResolver(Path.GetTempPath()), NullLogger<PurchaseOrderDocument>.Instance);

    [Fact, Trait("Id", "MM-UT-021"), Trait("Rule", "MM-BR-021")]
    public async Task Pdf_HasTheRevisionVendorLinesInOrderAndTotals()
    {
        var pdf = await Document(fixture.Renderer).PdfAsync(Print, PrintPaper.Letter, new CultureInfo("en"), CancellationToken.None);

        using var document = PdfDocument.Open(pdf);
        var text = Compact(string.Concat(document.GetPages().Select(p => p.Text)));
        text.Should().Contain("PurchaseOrder").And.Contain("PO2611000001").And.Contain("Rev.1").And.Contain("ACME")
            .And.Contain("CA90001").And.Contain("Fax555-0199").And.Contain("Delivertodock2");
        text.IndexOf("HexBolt", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("SteelPanel", StringComparison.Ordinal), "lines print in line order");
        text.Should().Contain("280.5000").And.Contain("1,332.37").And.Contain("1,465.61");
    }

    [Fact, Trait("Id", "MM-UC-003-T07")]
    public void Excel_HasTheSameContent_OnOneSheet()
    {
        var xlsx = Document(fixture.Renderer).Excel(Print, new CultureInfo("ko"));

        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = workbook.Worksheets.Single();
        sheet.Name.Should().Be("PO2611000001");
        var cells = sheet.CellsUsed().Select(c => c.GetFormattedString()).ToList();
        cells.Should().Contain(["발주 번호", "PO2611000001", "개정", "1", "공급자", "ACME", "Hex Bolt", "Steel Panel", "비고", "Deliver to dock 2"]);
        sheet.CellsUsed().Select(c => c.Value.ToString(CultureInfo.InvariantCulture)).Should().Contain(["1465.61", "280.5"]);
    }

    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Infrastructure.Reports;
using QuroErp.Infrastructure.Reports.Sd;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>Sales order, picking list and set component list (print-documents.md) as PDF with real Chromium.</summary>
public class SalesOrderDocumentTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private static readonly SalesOrderPrint Print = new(
        new PrintCompany("Quro Trading", ["1 Market St"], "555-0000", "office@example.com"),
        new SalesOrderPrintParty("Blue River Market", ["9 Lake Rd", "Austin TX 73301"], "555-0101"),
        new SalesOrderPrintParty("Blue River Dock", ["2 Pier Rd", "Dallas TX 75001"], "555-0102"),
        new SalesOrderDto(
            601, "SO2611000001", "SO", 801, "Blue River Market", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 7), new DateOnly(2026, 12, 5),
            "USD", 1m, "SHIP_AND_BILL", false, "DEMO_A", "NET_30", "UPS", "COLLECT", "Blue River Dock", "2 Pier Rd", null, "Dallas", "TX", "75001",
            "USA", "555-0102", null, "Leave at dock 2", "DLR", 909m, 90m, 999m, 999m,
            [
                new SalesOrderLineDto(9102, 2, 12, "KIT", "Starter Kit", 2m, 5m, 9m, "A", -10m, 5m, 1, 9m, 0m, 0m, 0m, null, "EA"),
                new SalesOrderLineDto(9101, 1, 11, "BOLT", "Hex Bolt", 10m, 100m, 900m, "A", -10m, 100m, 1, 900m, 90m, 0m, 0m, null, "EA"),
            ],
            DateTimeOffset.UnixEpoch),
        2, 2, 2);

    private static SalesOrderDocument Document(PdfRenderer renderer) =>
        new(renderer, new PrintTemplateResolver(Path.GetTempPath()), NullLogger<SalesOrderDocument>.Instance);

    [Fact, Trait("Id", "SD-UC-002-T06")]
    public async Task SalesOrder_HasPartiesLinesInOrderTotalsAndDiscountStatus()
    {
        var pdf = await Document(fixture.Renderer).SalesOrderPdfAsync(Print, PrintPaper.Letter, new CultureInfo("en"), CancellationToken.None);

        var text = Text(pdf);
        text.Should().Contain("SalesOrder").And.Contain("SO2611000001").And.Contain("BlueRiverMarket").And.Contain("BlueRiverDock")
            .And.Contain("DLR").And.Contain("Leaveatdock2").And.Contain("999.00");
        text.IndexOf("HexBolt", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("StarterKit", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "SD-UC-002-T06")]
    public async Task PickingList_HasLocationsAndPackingBoxes_ButNoAmounts()
    {
        var picking = new SalesOrderPickingPrint(Print, [new(1, "BOLT", "Hex Bolt", "EA", "A-01-02", 10m)]);

        var text = Text(await Document(fixture.Renderer).PickingListPdfAsync(picking, PrintPaper.Letter, new CultureInfo("en"), CancellationToken.None));

        text.Should().Contain("PickingList").And.Contain("A-01-02").And.Contain("PackingBy").And.Contain("10.00");
        text.Should().NotContain("999.00");
    }

    [Fact, Trait("Id", "SD-UC-002-T06")]
    public async Task SetComponentList_MultipliesTheOrderQtyByTheComponentQty()
    {
        var components = new SalesOrderSetComponentPrint(Print, [new(2, "KIT", "Starter Kit", 2m, "NUT", "Hex Nut", "EA", "B-02", 4m)]);

        var text = Text(await Document(fixture.Renderer).SetComponentListPdfAsync(
            components, PrintPaper.Letter, new CultureInfo("ko"), CancellationToken.None));

        text.Should().Contain("세트구성품목록").And.Contain("NUT").And.Contain("8.00");
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(string.Concat(document.GetPages().Select(p => p.Text)).Where(c => !char.IsWhiteSpace(c)));
    }
}

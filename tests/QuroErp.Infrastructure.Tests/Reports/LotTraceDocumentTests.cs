using System.Globalization;
using ClosedXML.Excel;
using FluentAssertions;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Infrastructure.Reports;
using QuroErp.Infrastructure.Reports.Mm;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>The Lot trace report (MM-UC-040, MM-BR-369) as PDF with real Chromium and as XLSX: the same rows as the screen.</summary>
public class LotTraceDocumentTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private static readonly DateOnly Day = new(2026, 11, 4);

    private static readonly LotTraceRowDto[] Rows =
    [
        new(1, null, 0, "LOT", "GR-001", 1, Day, null, null, "Vendor A", "CABLE", "Cable", "261104-001", null),
        new(2, 1, 1, "GR", "GR-001", 1, Day, "GR", "MAIN", "Vendor A", "CABLE", "Cable", "261104-001", 10m),
        new(3, 1, 1, "SHIP", "SH-001", 1, Day.AddDays(1), "DN", "MAIN", "Customer A", "CABLE", "Cable", "261104-001", -3m),
        new(4, 1, 1, "SHIP", "SH-002", 2, Day.AddDays(2), "DN", "MAIN", "Customer B", "CABLE", "Cable", "261104-001", -4m),
    ];

    private static readonly LotTracePrint Print = new(
        new PrintCompany("Demo Trading", [], null, null), LotTraceRoutes.Forward,
        [new LotTraceCondition("Print.LotTrace.Item", "CABLE"), new LotTraceCondition("Print.LotTrace.LotNo", "261104-001")], Rows, 2);

    private LotTraceDocument Document => new(fixture.Renderer, new ExcelWriter());

    [Fact, Trait("Id", "MM-UC-040-T04"), Trait("Id", "MM-UT-369"), Trait("Rule", "MM-BR-369")]
    public void Excel_HasOneRowPerScreenRow_WithTheSameValues()
    {
        var culture = new CultureInfo("en");

        using var workbook = new XLWorkbook(new MemoryStream(Document.Excel(Print, culture)));

        var sheet = workbook.Worksheets.Single();
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Level");
        var lines = sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber())
            .Select(r => (Level: r.Cell(1).GetValue<int>(), Type: r.Cell(2).GetString(), No: r.Cell(3).GetString(), Party: r.Cell(6).GetString(),
                Qty: r.Cell(10).IsEmpty() ? (decimal?)null : r.Cell(10).GetValue<decimal>()))
            .ToList();
        lines.Should().Equal(
            (0, "Lot", "GR-001-1", "Vendor A", null),
            (1, "Goods Receipt (GR)", "GR-001-1", "Vendor A", 10m),
            (1, "Shipment (DN)", "SH-001-1", "Customer A", -3m),
            (1, "Shipment (DN)", "SH-002-2", "Customer B", -4m));
        sheet.CellsUsed().Select(c => c.GetString()).Should().Contain(["Item", "CABLE", "Lot No.", "261104-001"]);
    }

    [Fact, Trait("Id", "MM-UC-040-T04"), Trait("Rule", "MM-BR-369")]
    public async Task Pdf_HasTheTitleConditionsAndRowsInOrder()
    {
        var pdf = await Document.PdfAsync(Print, PrintPaper.A4, new CultureInfo("ko"), CancellationToken.None);

        using var document = PdfDocument.Open(pdf);
        var text = string.Concat(document.GetPages().Select(p => p.Text)).Replace(" ", "", StringComparison.Ordinal);
        text.Should().Contain("Lot추적(정전개)").And.Contain("261104-001").And.Contain("CustomerA");
        text.IndexOf("SH-001-1", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("SH-002-2", StringComparison.Ordinal));
        text.Should().Contain("-3.00").And.Contain("10.00");
    }

    [Fact, Trait("Id", "MM-UT-369"), Trait("Rule", "MM-BR-369")]
    public void Excel_WithoutRows_StillHasTheHeader()
    {
        using var workbook = new XLWorkbook(new MemoryStream(Document.Excel(Print with { Rows = [] }, new CultureInfo("en"))));

        workbook.Worksheets.Single().RowsUsed().Should().Contain(r => r.Cell(1).GetString() == "Level");
    }
}

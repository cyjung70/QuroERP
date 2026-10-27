using ClosedXML.Excel;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Excel export of the grid columns (screens.md "툴바").</summary>
public class QuroExcelTests
{
    [Fact]
    public void Create_WritesHeaderAndValues()
    {
        // Columns are components; the test sets their parameters directly
#pragma warning disable BL0005
        QuroColumn<SampleItem>[] columns =
        [
            new() { Title = "코드", Value = r => r.Code },
            new() { Title = "적용일", Value = r => r.ValidFrom },
            new() { Title = "수량", Value = r => r.Qty },
        ];
#pragma warning restore BL0005
        SampleItem[] rows = [new() { Code = "A100", ValidFrom = new DateOnly(2026, 1, 2), Qty = 1.5m }];

        var bytes = QuroExcel.Create("품목:목록/샘플", columns, rows);

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheets.Single();
        sheet.Name.Should().NotContainAny(":", "/");
        sheet.Cell(1, 1).GetString().Should().Be("코드");
        sheet.Cell(2, 1).GetString().Should().Be("A100");
        sheet.Cell(2, 2).GetDateTime().Should().Be(new DateTime(2026, 1, 2));
        sheet.Cell(2, 3).GetDouble().Should().Be(1.5);
    }
}

using System.Globalization;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QuroErp.Application.Common.Printing;
using QuroErp.Infrastructure.Reports;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>Excel writer, template resolver, paper, options, labels and fonts (reports.md).</summary>
public class PrintFoundationTests
{
    [Fact, Trait("Id", "CORE-033-UT-01")]
    public void Excel_WritesNumberDateAndTextCells_AndFormulaTextStaysText()
    {
        var sheet = new ExcelSheet(
            "PO: 2611/01",
            [new ExcelField("발주 번호", "PO2611000001"), new ExcelField("합계", 1234.5m, 2)],
            [new ExcelColumn("품목"), new ExcelColumn("수량", 2), new ExcelColumn("납기일")],
            [["=1+1", 10m, new DateOnly(2026, 11, 30)], ["@SUM(A1)", 2.5m, null]]);

        using var workbook = new XLWorkbook(new MemoryStream(new ExcelWriter().Write(sheet)));
        var ws = workbook.Worksheets.Single();

        ws.Name.Should().Be("PO 261101");
        (ws.Cell("A1").GetString(), ws.Cell("B1").GetString()).Should().Be(("발주 번호", "PO2611000001"));
        (ws.Cell("B2").DataType, ws.Cell("B2").GetDouble(), ws.Cell("B2").Style.NumberFormat.Format).Should().Be((XLDataType.Number, 1234.5, "#,##0.00"));
        ws.Cell("A4").GetString().Should().Be("품목");
        ws.Cell("A4").Style.Font.Bold.Should().BeTrue();
        foreach (var cell in new[] { ws.Cell("A5"), ws.Cell("A6") })
        {
            (cell.HasFormula, cell.DataType).Should().Be((false, XLDataType.Text));
        }
        ws.Cell("A5").GetString().Should().Be("=1+1");
        ws.Cell("B5").DataType.Should().Be(XLDataType.Number);
        (ws.Cell("C5").DataType, ws.Cell("C5").GetDateTime()).Should().Be((XLDataType.DateTime, new DateTime(2026, 11, 30)));
        ws.Cell("C6").IsEmpty().Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-033-UT-02")]
    public void Resolver_UsesTheCustomFileWhenPresent_ElseHtml()
    {
        var root = Directory.CreateTempSubdirectory("quro-print-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mm"));
            File.WriteAllText(Path.Combine(root, "mm", "purchase-order.frx"), "<Report/>");
            var resolver = new PrintTemplateResolver(root);

            var custom = resolver.Resolve("mm", "purchase-order");
            var html = resolver.Resolve("sd", "sales-order");

            (custom.IsCustom, custom.CustomPath).Should().Be((true, Path.Combine(root, "mm", "purchase-order.frx")));
            html.Should().Be(PrintTemplate.Html);
            html.IsCustom.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory, Trait("Id", "CORE-033-UT-02")]
    [InlineData("..", "purchase-order")]
    [InlineData("mm", "../secret")]
    [InlineData("MM", "purchase-order")]
    [InlineData("mm", "")]
    public void Resolver_RejectsNamesOutsideTheFolder(string moduleCode, string document)
    {
        var act = () => new PrintTemplateResolver(Path.GetTempPath()).Resolve(moduleCode, document);

        act.Should().Throw<ArgumentException>();
    }

    [Theory, Trait("Id", "CORE-033-UT-03")]
    [InlineData("US", "Letter")]
    [InlineData("KR", "A4")]
    [InlineData("kr", "A4")]
    [InlineData(null, "Letter")]
    public void Paper_IsLetterForUsAndA4ForKr(string? country, string format)
    {
        PrintPaper.ForCountry(country).Format.Should().Be(format);
        PrintPaper.Margin.Should().Be("12mm");
    }

    [Fact]
    public void Options_DefaultTo4And30_AndReadThePrintSection()
    {
        var empty = new ConfigurationBuilder().Build();
        var set = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Print:MaxConcurrency"] = "2", ["Print:TimeoutSeconds"] = "0" })
            .Build();

        PrintOptions.From(empty).Should().Be(new PrintOptions(4, 30));
        PrintOptions.From(set).Should().Be(new PrintOptions(2, 30));
    }

    [Theory]
    [InlineData("ko", "전화")]
    [InlineData("en", "Tel")]
    public void Labels_ComeFromThePrintResources(string culture, string phone)
    {
        PrintText.Get("Print.Phone", CultureInfo.GetCultureInfo(culture)).Should().Be(phone);
        PrintText.Get("Print.Missing", CultureInfo.GetCultureInfo(culture)).Should().Be("Print.Missing");
    }

    [Fact]
    public void Fonts_ShipWithTheOflLicense_AndAreEmbeddedAsDataUris()
    {
        File.Exists(Path.Combine(PrintFonts.Folder, "OFL.txt")).Should().BeTrue();
        PrintFonts.StyleElement.Should().Contain("font-family:'IBM Plex Sans KR';font-weight:700").And.Contain("IBM Plex Mono")
            .And.Contain("url(data:font/ttf;base64,").And.NotContain("url(http");
    }
}

using System.Globalization;
using ClosedXML.Excel;
using Microsoft.JSInterop;

namespace QuroErp.Web.Components.Screens;

/// <summary>Excel export of a Multi (M) grid (screens.md "툴바": 엑셀 내보내기). One sheet, the grid's columns and rows.</summary>
public static class QuroExcel
{
    // Excel limits sheet names to 31 characters
    private const int MaxSheetName = 31;

    public static byte[] Create<TRow>(string sheetName, IReadOnlyList<QuroColumn<TRow>> columns, IEnumerable<TRow> rows)
        where TRow : class
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(Clean(sheetName));
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c].Title;
        }
        sheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                sheet.Cell(r, c + 1).Value = XLCellValue.FromObject(Excel(columns[c].Value?.Invoke(row)), CultureInfo.InvariantCulture);
            }
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Starts a browser download (wwwroot/js/quro.js).</summary>
    public static async Task DownloadAsync(IJSRuntime js, string fileName, byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reference = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("quroDownload", fileName, reference);
    }

    // DateOnly is not an Excel value type
    private static object? Excel(object? value) => value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value;

    private static string Clean(string name)
    {
        var cleaned = new string([.. name.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']'))]);
        return cleaned.Length switch
        {
            0 => "Sheet1",
            > MaxSheetName => cleaned[..MaxSheetName],
            _ => cleaned,
        };
    }
}

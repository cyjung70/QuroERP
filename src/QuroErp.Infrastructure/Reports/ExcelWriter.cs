using ClosedXML.Excel;
using QuroErp.Application.Common.Printing;

namespace QuroErp.Infrastructure.Reports;

/// <summary>
/// ClosedXML writer shared by reports and documents (reports.md "엑셀"): one sheet, header fields in columns A and B, a blank
/// row, then the bold column titles and the lines. Strings are written as text, so nothing is parsed as a formula.
/// </summary>
public sealed class ExcelWriter : IExcelWriter
{
    // Excel limits sheet names to 31 characters and forbids : \ / ? * [ ]
    private const int MaxSheetName = 31;
    private static readonly char[] SheetNameForbidden = [':', '\\', '/', '?', '*', '[', ']'];

    public byte[] Write(ExcelSheet sheet)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SheetName(sheet.Name));

        var row = 1;
        foreach (var field in sheet.Fields)
        {
            Set(ws.Cell(row, 1), field.Label, null);
            Set(ws.Cell(row, 2), field.Value, field.Decimals);
            row++;
        }
        if (sheet.Fields.Count > 0)
        {
            ws.Range(1, 1, sheet.Fields.Count, 1).Style.Font.Bold = true;
            row++;
        }

        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            Set(ws.Cell(row, c + 1), sheet.Columns[c].Title, null);
        }
        ws.Row(row).Style.Font.Bold = true;
        row++;

        foreach (var values in sheet.Rows)
        {
            for (var c = 0; c < sheet.Columns.Count; c++)
            {
                Set(ws.Cell(row, c + 1), c < values.Count ? values[c] : null, sheet.Columns[c].Decimals);
            }
            row++;
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Set(IXLCell cell, object? value, int? decimals)
    {
        switch (value)
        {
            case null:
                cell.Value = Blank.Value;
                break;
            case decimal or double or float or int or long or short or byte:
                cell.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                if (decimals is { } d)
                {
                    cell.Style.NumberFormat.Format = d > 0 ? "#,##0." + new string('0', d) : "#,##0";
                }
                break;
            case DateOnly date:
                SetDate(cell, date.ToDateTime(TimeOnly.MinValue));
                break;
            case DateTime dateTime:
                SetDate(cell, dateTime);
                break;
            case bool flag:
                cell.Value = flag;
                break;
            default:
                // XLCellValue from a string is always text; =, +, - and @ stay text (security.md)
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private static void SetDate(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "yyyy-mm-dd";
    }

    private static string SheetName(string name)
    {
        var clean = new string([.. name.Where(ch => !SheetNameForbidden.Contains(ch))]).Trim();
        return clean.Length == 0 ? "Sheet1" : clean[..Math.Min(clean.Length, MaxSheetName)];
    }
}

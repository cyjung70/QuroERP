namespace QuroErp.Application.Common.Printing;

/// <summary>
/// A column of the line table. Title is already in the user's language (from a resource key). Decimals formats number cells;
/// null shows the value as stored.
/// </summary>
public sealed record ExcelColumn(string Title, int? Decimals = null);

/// <summary>A header field above the line table: label in the user's language and its value.</summary>
public sealed record ExcelField(string Label, object? Value, int? Decimals = null);

/// <summary>
/// One sheet (print-documents.md "엑셀"): header fields at the top, then the line table. Values in a row follow the column order.
/// </summary>
public sealed record ExcelSheet(
    string Name, IReadOnlyList<ExcelField> Fields, IReadOnlyList<ExcelColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>
/// XLSX of a report or document (reports.md "엑셀"). Numbers are number cells, DateOnly and DateTime date cells, everything else
/// text cells: a string that starts with =, +, - or @ stays text and is never a formula (security.md "출력 인코딩").
/// </summary>
public interface IExcelWriter
{
    byte[] Write(ExcelSheet sheet);
}

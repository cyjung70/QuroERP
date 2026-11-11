using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Sd.SalesOrders;

/// <summary>One usable row of a line file: its line number, item code or barcode, and quantity.</summary>
public sealed record SalesOrderFileRow(int LineNo, string Key, decimal Qty);

/// <summary>A row the file cannot use (SD-BR-019): line number, the value as read and the reason code.</summary>
public sealed record SalesOrderFileError(int LineNo, string Value, string Reason);

/// <summary>
/// Sales order line files (SD-BR-017~022): UTF-8 CSV text, one "item code,qty" or "barcode,qty" per line, no heading, blank lines
/// ignored, at most 1 MB and 1,000 lines. Rows with "." as quantity are skipped. The rows only fill the screen grid; saving
/// follows the order save rules.
/// </summary>
public static class SalesOrderLineFile
{
    /// <summary>The file is too large or has too many lines (SD-BR-017). Args: max bytes, max lines.</summary>
    public const string FileCode = "SD-E-017";

    /// <summary>Some rows cannot be used, so no row is added (SD-BR-019). Arg: number of error rows.</summary>
    public const string ErrorRowsCode = "SD-E-019";

    /// <summary>Reason: the row is not "key,qty".</summary>
    public const string FormatReason = "FORMAT";

    /// <summary>Reason: the quantity is not a number above 0 (SD-BR-018).</summary>
    public const string QtyReason = "QTY";

    /// <summary>Reason: no item with this code or barcode (SD-BR-019).</summary>
    public const string NotFoundReason = "NOT_FOUND";

    /// <summary>Reason: the item is not in use (SD-BR-019).</summary>
    public const string NotInUseReason = "NOT_IN_USE";

    public const int MaxBytes = 1024 * 1024;
    public const int MaxLines = 1000;

    /// <summary>Reads the rows and the format and quantity errors (SD-BR-017, SD-BR-018).</summary>
    /// <exception cref="DomainException">SD-E-017 when the file is over 1 MB or 1,000 lines.</exception>
    public static (IReadOnlyList<SalesOrderFileRow> Rows, IReadOnlyList<SalesOrderFileError> Errors) Parse(string text, long byteLength)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (byteLength > MaxBytes || lines.Count(l => !string.IsNullOrWhiteSpace(l)) > MaxLines)
        {
            throw new DomainException(FileCode, MaxBytes, MaxLines);
        }
        var rows = new List<SalesOrderFileRow>();
        var errors = new List<SalesOrderFileError>();
        foreach (var (raw, index) in lines.Select((l, i) => (l.Trim().TrimStart('﻿'), i + 1)))
        {
            if (raw.Length == 0)
            {
                continue;
            }
            var parts = raw.Split(',');
            if (parts.Length != 2 || parts[0].Trim().Length == 0)
            {
                errors.Add(new SalesOrderFileError(index, raw, FormatReason));
                continue;
            }
            var qtyText = parts[1].Trim();
            if (qtyText == ".")
            {
                continue;
            }
            if (!decimal.TryParse(qtyText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                errors.Add(new SalesOrderFileError(index, raw, QtyReason));
                continue;
            }
            rows.Add(new SalesOrderFileRow(index, parts[0].Trim(), qty));
        }
        return (rows, errors);
    }
}

using System.Globalization;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Mm.Lots;
using Template = QuroErp.Infrastructure.Reports.Templates.Mm.LotTrace;

namespace QuroErp.Infrastructure.Reports.Mm;

/// <summary>Texts of a Lot trace row shared by the PDF and the sheet, so both show the same values.</summary>
public static class LotTraceText
{
    /// <summary>The row kind, and for a movement row its movement type: "Shipment (DN)".</summary>
    public static string DocType(LotTraceRowDto row, CultureInfo culture)
    {
        var kind = PrintText.Get("Print.LotTrace.DocType." + row.DocType, culture);
        return row.MovType is { Length: > 0 } type ? $"{kind} ({type})" : kind;
    }

    /// <summary>The document number with its line: "SH-0001-2".</summary>
    public static string? DocNo(LotTraceRowDto row) =>
        row.DocNo is null ? null : row.DocSeq is { } seq ? $"{row.DocNo}-{seq.ToString(CultureInfo.InvariantCulture)}" : row.DocNo;
}

/// <summary>Lot trace report: the HTML template as PDF, and the same rows as one XLSX sheet (MM-BR-369).</summary>
public sealed class LotTraceDocument(IPdfRenderer pdf, IExcelWriter excel) : ILotTraceDocument
{
    public Task<byte[]> PdfAsync(LotTracePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        pdf.RenderAsync(typeof(Template), new Dictionary<string, object?> { ["Print"] = print, ["Culture"] = culture }, paper, cancellationToken);

    public byte[] Excel(LotTracePrint print, CultureInfo culture)
    {
        string T(string key) => PrintText.Get(key, culture);
        var fields = print.Conditions.Select(c => new ExcelField(T(c.LabelKey), c.Value)).ToList();
        ExcelColumn[] columns =
        [
            new(T("Print.LotTrace.Level")), new(T("Print.LotTrace.DocType")), new(T("Print.LotTrace.DocNo")), new(T("Print.LotTrace.DocDt")),
            new(T("Print.LotTrace.Warehouse")), new(T("Print.LotTrace.Party")), new(T("Print.LotTrace.Item")), new(T("Print.LotTrace.ItemName")),
            new(T("Print.LotTrace.LotNo")), new(T("Print.LotTrace.Qty"), print.QtyDecimals),
        ];
        var rows = print.Rows
            .Select(r => (IReadOnlyList<object?>)
                [r.Level, LotTraceText.DocType(r, culture), LotTraceText.DocNo(r), r.DocDt, r.WarehouseCode, r.PartyName, r.ItemCd, r.ItemName, r.LotNo, r.Qty])
            .ToList();
        return excel.Write(new ExcelSheet(T("Print.LotTrace.Title." + print.Direction), fields, columns, rows));
    }
}

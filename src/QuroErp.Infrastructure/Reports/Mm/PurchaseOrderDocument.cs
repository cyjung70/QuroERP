using System.Globalization;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using Template = QuroErp.Infrastructure.Reports.Templates.Mm.PurchaseOrder;

namespace QuroErp.Infrastructure.Reports.Mm;

/// <summary>
/// Purchase order form: the HTML template as PDF, and the same content as one XLSX sheet (print-documents.md). A customer
/// template (Reports/Custom/mm/purchase-order.frx) is not printed yet: FastReport comes with the first one, so the standard
/// template is used and a warning is logged.
/// </summary>
public sealed partial class PurchaseOrderDocument(
    IPdfRenderer pdf, IExcelWriter excel, IPrintTemplateResolver templates, ILogger<PurchaseOrderDocument> logger) : IPurchaseOrderDocument
{
    public const string ModuleCode = "mm";
    public const string Document = "purchase-order";

    public Task<byte[]> PdfAsync(PurchaseOrderPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (templates.Resolve(ModuleCode, Document) is { IsCustom: true } custom)
        {
            CustomTemplateNotSupported(logger, custom.CustomPath!);
        }
        return pdf.RenderAsync(typeof(Template), new Dictionary<string, object?> { ["Print"] = print, ["Culture"] = culture }, paper, cancellationToken);
    }

    public byte[] Excel(PurchaseOrderPrint print, CultureInfo culture)
    {
        string T(string key) => PrintText.Get(key, culture);
        var order = print.Order;
        var fields = new List<ExcelField>
        {
            new(T("Print.PurchaseOrder.PoNo"), order.PoNo),
        };
        if (order.RevisionNo >= 1)
        {
            fields.Add(new(T("Print.PurchaseOrder.RevisionNo"), order.RevisionNo));
        }
        fields.AddRange(
        [
            new(T("Print.PurchaseOrder.Vendor"), print.Vendor.Name),
            new(T("Print.PurchaseOrder.PoDt"), order.PoDt),
            new(T("Print.PurchaseOrder.DueDt"), order.DueDt),
            new(T("Print.PurchaseOrder.Currency"), order.CurrencyCode),
            new(T("Print.PurchaseOrder.Incoterms"), order.Incoterms),
            new(T("Print.PurchaseOrder.PaymentTerms"), order.PaymentTerms),
            new(T("Print.PurchaseOrder.Buyer"), order.Buyer),
            new(T("Print.PurchaseOrder.Subtotal"), order.GrossAmt, print.AmountDecimals),
            new(T("Print.PurchaseOrder.Discount"), order.DcAmt, print.AmountDecimals),
            new(T("Print.PurchaseOrder.Tax"), order.TaxAmt, print.AmountDecimals),
            new(T("Print.PurchaseOrder.Total"), order.TotPoAmt, print.AmountDecimals),
            new(T("Print.PurchaseOrder.Remarks"), order.Remarks),
        ]);
        ExcelColumn[] columns =
        [
            new(T("Print.PurchaseOrder.Seq")), new(T("Print.PurchaseOrder.ItemCode")), new(T("Print.PurchaseOrder.ItemName")),
            new(T("Print.PurchaseOrder.Unit")), new(T("Print.PurchaseOrder.Qty"), print.QtyDecimals),
            new(T("Print.PurchaseOrder.UnitPrice"), print.PriceDecimals), new(T("Print.PurchaseOrder.DcRate"), 2),
            new(T("Print.PurchaseOrder.Amount"), print.AmountDecimals), new(T("Print.PurchaseOrder.LineDueDt")),
        ];
        var rows = order.Lines.OrderBy(l => l.Seq)
            .Select(l => (IReadOnlyList<object?>)[l.Seq, l.ItemCd, l.ItemName, l.Unit, l.PoQty, l.PoPrc, l.DcRate, l.PoAmt, l.DueDt])
            .ToList();
        return excel.Write(new ExcelSheet(order.PoNo, fields, columns, rows));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Customer template {Path} is not supported yet; printing the standard template.")]
    private static partial void CustomTemplateNotSupported(ILogger logger, string path);
}

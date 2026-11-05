using System.Globalization;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Sd.SalesInvoices;
using SalesInvoiceTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.SalesInvoice;
using TaxInvoiceTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.TaxInvoice;

namespace QuroErp.Infrastructure.Reports.Sd;

/// <summary>
/// Sales invoice and KR tax invoice as PDF from the HTML templates. Customer templates (Reports/Custom/sd/*.frx) are not printed
/// yet: the standard template is used and a warning is logged.
/// </summary>
public sealed partial class SalesInvoiceDocument(IPdfRenderer pdf, IPrintTemplateResolver templates, ILogger<SalesInvoiceDocument> logger)
    : ISalesInvoiceDocument
{
    public Task<byte[]> SalesInvoicePdfAsync(SalesInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        RenderAsync("sales-invoice", typeof(SalesInvoiceTemplate), print, paper, culture, cancellationToken);

    public Task<byte[]> TaxInvoicePdfAsync(TaxInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        RenderAsync("tax-invoice", typeof(TaxInvoiceTemplate), print, paper, culture, cancellationToken);

    private Task<byte[]> RenderAsync(string document, Type template, object print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (templates.Resolve(SalesOrderDocument.ModuleCode, document) is { IsCustom: true } custom)
        {
            CustomTemplateNotSupported(logger, custom.CustomPath!);
        }
        return pdf.RenderAsync(template, new Dictionary<string, object?> { ["Print"] = print, ["Culture"] = culture }, paper, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Customer template {Path} is not supported yet; printing the standard template.")]
    private static partial void CustomTemplateNotSupported(ILogger logger, string path);
}

using System.Globalization;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Sd.SalesOrders;
using PickingTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.PickingList;
using SalesOrderTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.SalesOrder;
using SetComponentTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.SetComponentList;

namespace QuroErp.Infrastructure.Reports.Sd;

/// <summary>
/// Sales order, picking list and set component list as PDF from the HTML templates (print-documents.md). Customer templates
/// (Reports/Custom/sd/*.frx) are not printed yet: the standard template is used and a warning is logged.
/// </summary>
public sealed partial class SalesOrderDocument(IPdfRenderer pdf, IPrintTemplateResolver templates, ILogger<SalesOrderDocument> logger)
    : ISalesOrderDocument
{
    public const string ModuleCode = "sd";

    public Task<byte[]> SalesOrderPdfAsync(SalesOrderPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        RenderAsync("sales-order", typeof(SalesOrderTemplate), print, paper, culture, cancellationToken);

    public Task<byte[]> PickingListPdfAsync(SalesOrderPickingPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        RenderAsync("picking-list", typeof(PickingTemplate), print, paper, culture, cancellationToken);

    public Task<byte[]> SetComponentListPdfAsync(
        SalesOrderSetComponentPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
        RenderAsync("set-component-list", typeof(SetComponentTemplate), print, paper, culture, cancellationToken);

    private Task<byte[]> RenderAsync(string document, Type template, object print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (templates.Resolve(ModuleCode, document) is { IsCustom: true } custom)
        {
            CustomTemplateNotSupported(logger, custom.CustomPath!);
        }
        return pdf.RenderAsync(template, new Dictionary<string, object?> { ["Print"] = print, ["Culture"] = culture }, paper, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Customer template {Path} is not supported yet; printing the standard template.")]
    private static partial void CustomTemplateNotSupported(ILogger logger, string path);
}

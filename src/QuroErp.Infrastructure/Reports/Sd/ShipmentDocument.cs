using System.Globalization;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Sd.Shipments;
using DeliveryNoteTemplate = QuroErp.Infrastructure.Reports.Templates.Sd.DeliveryNote;

namespace QuroErp.Infrastructure.Reports.Sd;

/// <summary>
/// Delivery note as PDF from the HTML template. A customer template (Reports/Custom/sd/delivery-note.frx) is not printed yet: the
/// standard template is used and a warning is logged.
/// </summary>
public sealed partial class ShipmentDocument(IPdfRenderer pdf, IPrintTemplateResolver templates, ILogger<ShipmentDocument> logger)
    : IShipmentDocument
{
    public Task<byte[]> DeliveryNotePdfAsync(ShipmentPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (templates.Resolve(SalesOrderDocument.ModuleCode, "delivery-note") is { IsCustom: true } custom)
        {
            CustomTemplateNotSupported(logger, custom.CustomPath!);
        }
        return pdf.RenderAsync(
            typeof(DeliveryNoteTemplate), new Dictionary<string, object?> { ["Print"] = print, ["Culture"] = culture }, paper, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Customer template {Path} is not supported yet; printing the standard template.")]
    private static partial void CustomTemplateNotSupported(ILogger logger, string path);
}

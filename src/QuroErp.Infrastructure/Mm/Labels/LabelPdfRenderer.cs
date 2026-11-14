using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.Labels;
using QuroErp.Domain.Mm.Labels;
using Template = QuroErp.Infrastructure.Reports.Templates.Mm.LabelSheet;

namespace QuroErp.Infrastructure.Mm.Labels;

/// <summary>Labels as PDF pages of the format's size through the shared PDF engine (TASK-CORE-033, MM-BR-919).</summary>
public sealed class LabelPdfRenderer(IPdfRenderer pdf) : ILabelPdfRenderer
{
    public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken) =>
        pdf.RenderAsync(
            typeof(Template),
            new Dictionary<string, object?> { ["Format"] = format, ["Pages"] = pages, ["Copies"] = copies },
            PrintPaper.Label(format.WidthMm, format.HeightMm),
            cancellationToken);
}

using System.Data;
using System.Text;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>One row of mm_label_print (MM-BR-919).</summary>
public sealed record LabelPrintRow(string LabelType, long RefId, int Copies, string OutputKind, string? PrinterName, string PrintedBy);

/// <summary>The print history (mm_label_print).</summary>
public interface ILabelPrintRepository
{
    Task AddAsync(IReadOnlyList<LabelPrintRow> rows, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>Labels as PDF pages of the label size (TASK-CORE-033 PDF engine).</summary>
public interface ILabelPdfRenderer
{
    Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken);
}

/// <summary>The printed output: ZPL text for a Zebra printer or a PDF.</summary>
public sealed record LabelOutput(string OutputKind, string ContentType, byte[] Content);

/// <summary>
/// Prints labels in the format's output kind and keeps the history (MM-BR-917~919). The label print API and screens (TASK-MM-057,
/// TASK-MM-058) read the data and call this.
/// </summary>
public sealed class LabelPrintService(ILabelPdfRenderer pdf, ILabelPrintRepository history, IUnitOfWorkFactory unitOfWork)
{
    public const string ZplContentType = "application/vnd.zebra-zpl";
    public const string PdfContentType = "application/pdf";

    /// <summary>
    /// Renders the labels of one type in order (a Pallet label, then its DataMatrix label, MM-BR-918) and saves one history row
    /// per label.
    /// </summary>
    /// <exception cref="ArgumentException">A label of another type than the format, or copies not above zero.</exception>
    public async Task<LabelOutput> PrintAsync(
        LabelFormat format, IReadOnlyList<Label> labels, int copies, string? printerName, string user, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copies);
        if (labels.Any(l => l.LabelType != format.LabelType))
        {
            throw new ArgumentException($"Every label must be a {format.LabelType} label.", nameof(labels));
        }
        var pages = LabelPages.For(labels);
        var output = format.OutputKind == LabelType.Zpl
            ? new LabelOutput(LabelType.Zpl, ZplContentType, Encoding.UTF8.GetBytes(ZplLabelWriter.Write(format, pages, copies)))
            : new LabelOutput(LabelType.Pdf, PdfContentType, await pdf.RenderAsync(format, pages, copies, cancellationToken));

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await history.AddAsync(
            [.. labels.Select(l => new LabelPrintRow(l.LabelType, l.RefId, copies, output.OutputKind, printerName, user))],
            uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return output;
    }
}

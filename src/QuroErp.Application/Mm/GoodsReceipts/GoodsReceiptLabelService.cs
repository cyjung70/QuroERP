using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.GoodsReceipts;

/// <summary>
/// The Pallet labels of a goods receipt (MM-BR-918, MM-BR-919): the labels of its ACTIVE Pallets, printed as a PDF with the size
/// and fields of the PALLET label format. Every print is kept in the history and can be printed again.
/// </summary>
public sealed class GoodsReceiptLabelService(
    IGoodsReceiptRepository receipts,
    IPalletRepository pallets,
    ILabelFormatRepository formats,
    LabelPrintService labels,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The receipt, its ACTIVE Pallets or the PALLET label format does not exist. Arg: receipt id or label type.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<LabelOutput> PrintPalletLabelsAsync(long id, string user, CancellationToken cancellationToken)
    {
        LabelFormat format;
        IReadOnlyList<PalletLabelRow> rows;
        await using (var uow = await unitOfWork.BeginAsync(cancellationToken))
        {
            _ = await receipts.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
            format = await formats.FindAsync(LabelType.Pallet, uow.Transaction, cancellationToken)
                ?? throw new NotFoundException(NotFoundCode, LabelType.Pallet);
            rows = await pallets.FindReceiptLabelsAsync(id, uow.Transaction, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }
        if (rows.Count == 0)
        {
            throw new NotFoundException(NotFoundCode, id);
        }
        // The receipt screen prints Pallet labels as a PDF, whatever output the format has
        return await labels.PrintAsync(format with { OutputKind = LabelType.Pdf }, [.. rows.Select(Label)], 1, null, user, cancellationToken);
    }

    /// <summary>A Pallet label with the values of the PALLET format fields (bc_label_format seed) and its DataMatrix.</summary>
    public static Label Label(PalletLabelRow row) =>
        LabelValues.Pallet(row.Id, row.PalletNo, row.ItemCd, row.ItemName, row.LotNo, row.Qty, row.ExpiryDt);
}

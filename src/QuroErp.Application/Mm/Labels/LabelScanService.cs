using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>
/// Reads a scanned value for the PDA screens (MM-BR-923): the own-format DataMatrix (MM-BR-918), or a value without a kind looked
/// up as an item barcode, then an item code. Read only; the screen saves through the PC APIs.
/// </summary>
public sealed class LabelScanService(ILabelScanRepository repository, IUnitOfWorkFactory unitOfWork)
{
    /// <exception cref="DomainException">
    /// MM-E-923 with the scanned value when it is not a label of this system, its item is not found, or the step does not take its kind.
    /// </exception>
    public async Task<LabelScanDto> ReadAsync(LabelScanQuery query, CancellationToken cancellationToken)
    {
        var scan = LabelCode.Parse(query.Value);
        var scanned = query.Value!.Trim();
        var accepted = query.Accept ?? [];
        if (scan.Kind == LabelCode.Location)
        {
            LabelCode.EnsureAccepted(scan.Kind, accepted, scanned);
            return new LabelScanDto(scan.Kind, scan.Parts, null, null, null);
        }
        // A plain value is an item once it is found, so the step check waits for the lookup
        if (scan.Kind != LabelCode.Plain)
        {
            LabelCode.EnsureAccepted(scan.Kind, accepted, scanned);
        }

        LabelScanItem? item;
        await using (var uow = await unitOfWork.BeginAsync(cancellationToken))
        {
            var tx = uow.Transaction;
            item = scan.Kind == LabelCode.Plain
                ? await repository.FindItemByBarcodeAsync(scan.Parts[0], tx, cancellationToken)
                    ?? await repository.FindItemByCodeAsync(scan.Parts[0], tx, cancellationToken)
                : await repository.FindItemByCodeAsync(LabelCode.ItemCodeOf(scan)!, tx, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }
        if (item is null)
        {
            throw new DomainException(LabelCode.UnknownCode, scanned);
        }
        if (scan.Kind == LabelCode.Plain)
        {
            LabelCode.EnsureAccepted(LabelCode.Item, accepted, scanned);
            return new LabelScanDto(LabelCode.Item, [item.ItemCd], item.Id, item.ItemCd, item.ItemName);
        }
        return new LabelScanDto(scan.Kind, scan.Parts, item.Id, item.ItemCd, item.ItemName);
    }
}

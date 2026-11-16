using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Application.Mm.Lots;

/// <summary>
/// Lot trace (MM-UC-040): forward from a Lot to the customers it reached, backward from an issue to the vendors its Lots came
/// from, across Set assembly one step (MM-BR-366~368). Read only. The screen, PDF and Excel use these rows (MM-BR-369).
/// </summary>
public sealed class LotTraceService(ILotTraceRepository repository, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>A required condition is missing. Arg: its name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>A condition has a wrong value. Args: its name, the value.</summary>
    public const string InvalidCode = "Common-E-002";

    /// <summary>The Lot and where it went. No rows for Lot '*' or a Lot the item does not have.</summary>
    /// <exception cref="DomainException">Common-E-005 when itemId or lotNo is missing.</exception>
    public async Task<IReadOnlyList<LotTraceRowDto>> ForwardAsync(LotTraceForwardQuery query, CancellationToken cancellationToken)
    {
        var itemId = query.ItemId ?? throw new DomainException(RequiredCode, "itemId");
        var lotNo = string.IsNullOrWhiteSpace(query.LotNo) ? throw new DomainException(RequiredCode, "lotNo") : query.LotNo.Trim();
        if (!LotTraceRules.IsTraceable(lotNo))
        {
            return [];
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var lot = await repository.FindLotAsync(itemId, lotNo, tx, cancellationToken);
        if (lot is null)
        {
            await uow.CommitAsync(cancellationToken);
            return [];
        }
        var links = (await repository.FindLinksAsync([lot.Id], tx, cancellationToken)).Where(l => l.ComponentLotId == lot.Id).ToList();
        var setLotIds = links.Select(l => l.SetLotId).Distinct().ToList();
        var setLots = setLotIds.Count == 0 ? [] : await repository.FindLotsAsync(setLotIds, tx, cancellationToken);
        var movements = await repository.FindMovementsAsync([lot.Id, .. setLotIds], tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. LotTraceRules.Forward(lot, movements, links, setLots).Select(ToDto)];
    }

    /// <summary>The issues the condition names and where their Lots came from.</summary>
    /// <exception cref="DomainException">
    /// Common-E-005 when no condition is given or a customer comes without from or to; Common-E-002 when from is after to.
    /// </exception>
    public async Task<IReadOnlyList<LotTraceRowDto>> BackwardAsync(LotTraceBackwardQuery query, CancellationToken cancellationToken)
    {
        var source = SourceOf(query);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var issues = await repository.FindIssuesAsync(source, tx, cancellationToken);
        var lotIds = issues.Where(i => LotTraceRules.IsTraceable(i.LotNo)).Select(i => i.LotId).Distinct().ToList();
        IReadOnlyList<LotTraceLot> lots = [];
        IReadOnlyList<LotTraceLink> links = [];
        if (lotIds.Count > 0)
        {
            links = [.. (await repository.FindLinksAsync(lotIds, tx, cancellationToken)).Where(l => lotIds.Contains(l.SetLotId))];
            lots = await repository.FindLotsAsync([.. lotIds.Concat(links.Select(l => l.ComponentLotId)).Distinct()], tx, cancellationToken);
        }
        await uow.CommitAsync(cancellationToken);
        return [.. LotTraceRules.Backward(issues, lots, links).Select(ToDto)];
    }

    // The first condition given wins: document number, shipment line, invoice line, then customer and period (MM-UC-040)
    private static LotTraceIssueSource SourceOf(LotTraceBackwardQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.DocNo))
        {
            return new(DocNo: query.DocNo.Trim().ToUpperInvariant());
        }
        if (query.ShipmentLineId is { } shipmentLineId)
        {
            return new(ShipmentLineId: shipmentLineId);
        }
        if (query.InvoiceLineId is { } invoiceLineId)
        {
            return new(InvoiceLineId: invoiceLineId);
        }
        var customerId = query.CustomerId ?? throw new DomainException(RequiredCode, "docNo");
        var from = query.From ?? throw new DomainException(RequiredCode, "from");
        var to = query.To ?? throw new DomainException(RequiredCode, "to");
        if (from > to)
        {
            throw new DomainException(InvalidCode, "to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        return new(CustomerId: customerId, From: from, To: to);
    }

    private static LotTraceRowDto ToDto(LotTraceRow r) => new(
        r.Key, r.ParentKey, r.Level, r.DocType, r.DocNo, r.DocSeq, r.DocDt, r.MovType, r.WarehouseCode, r.PartyName, r.ItemCd, r.ItemName,
        r.LotNo, r.Qty);
}

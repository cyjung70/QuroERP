using System.Data;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Application.Mm.Lots;

/// <summary>
/// Where an issue of the backward trace comes from (MM-BR-367): a shipment or invoice number, a shipment line, an invoice line or
/// a customer's documents dated From to To. Exactly one is set; From and To come with the customer.
/// </summary>
public sealed record LotTraceIssueSource(
    string? DocNo = null, long? ShipmentLineId = null, long? InvoiceLineId = null, long? CustomerId = null, DateOnly? From = null,
    DateOnly? To = null);

/// <summary>Reads of the Lot trace (MM-UC-040): Lots, their stock movements and the assembly links between them.</summary>
public interface ILotTraceRepository
{
    /// <summary>The Lot of the item with that number, or null.</summary>
    Task<LotTraceLot?> FindLotAsync(long itemId, string lotNo, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<IReadOnlyList<LotTraceLot>> FindLotsAsync(IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Every stock movement of the Lots, reversals included, oldest first.</summary>
    Task<IReadOnlyList<LotTraceMovement>> FindMovementsAsync(
        IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The links of open assemblies whose Set Lot or component Lot is one of the Lots (MM-BR-368).</summary>
    Task<IReadOnlyList<LotTraceLink>> FindLinksAsync(IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// The stock movements of the shipment and invoice lines the source names: a shipment's delivery (DN) and an invoice's own
    /// issue (bill first, TR), with their reversals. An invoice line of a shipped line gives that shipment line's movements.
    /// </summary>
    Task<IReadOnlyList<LotTraceMovement>> FindIssuesAsync(
        LotTraceIssueSource source, IDbTransaction transaction, CancellationToken cancellationToken);
}

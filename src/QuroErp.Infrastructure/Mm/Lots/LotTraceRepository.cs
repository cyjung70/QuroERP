using System.Data;
using Dapper;
using QuroErp.Application.Mm.Lots;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Infrastructure.Mm.Lots;

/// <summary>
/// Reads of the Lot trace (MM-UC-040). A movement names its document by type + number (ref_key) + line (ref_seq): a receipt
/// (GR), a shipment (DN), an invoice or Credit Memo that moved stock itself (TR under its number), an assembly (BP, BI, DA, DR).
/// </summary>
internal sealed class LotTraceRepository : ILotTraceRepository
{
    // The origin of a Lot: the receipt line that created it, or the assembly that made it as a Set Lot
    private const string SelectLots = """
        SELECT l.id, l.item_id, i.item_cd, i.item_name, l.lot_no, l.received_dt,
               r.gr_no AS receipt_no, rl.seq AS receipt_seq, r.gr_dt AS receipt_dt, v.v_name AS vendor_name, rl.gr_qty AS receipt_qty,
               a.assembly_no, a.assembly_dt, a.qty AS assembly_qty
        FROM mm_lot l
        JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN mm_goods_receipt_line rl ON rl.id = l.goods_receipt_line_id
        LEFT JOIN mm_goods_receipt r ON r.id = rl.goods_receipt_id
        LEFT JOIN bc_vendor v ON v.id = r.vendor_id
        LEFT JOIN mm_assembly a ON a.id = l.assembly_id
        """;

    private const string SelectLot = SelectLots + """

        WHERE l.item_id = @ItemId AND l.lot_no = @LotNo
        """;

    private const string SelectLotsById = SelectLots + """

        WHERE l.id = ANY(@Ids)
        """;

    // The document of a movement is looked up by its type, so equal numbers of different documents do not mix
    private const string SelectMovements = """
        SELECT g.lot_id, g.mov_type, g.ref_key, g.ref_seq, g.document_dt, g.warehouse_id, w.warehouse_code,
               g.item_id, i.item_cd, i.item_name, l.lot_no, g.qty,
               CASE WHEN r.id IS NOT NULL THEN 'GR' WHEN s.id IS NOT NULL THEN 'SHIP' WHEN v.id IS NOT NULL THEN 'INV'
                    WHEN a.id IS NOT NULL THEN 'ASM' ELSE 'MOVE' END AS doc_type,
               coalesce(rv.v_name, sc.c_name, vc.c_name) AS party_name
        FROM mm_goods_movement g
        JOIN bc_item i ON i.id = g.item_id
        JOIN mm_lot l ON l.id = g.lot_id
        JOIN bc_warehouse w ON w.id = g.warehouse_id
        LEFT JOIN mm_goods_receipt r ON g.mov_type = 'GR' AND r.gr_no = g.ref_key
        LEFT JOIN bc_vendor rv ON rv.id = r.vendor_id
        LEFT JOIN sd_shipment s ON g.mov_type = 'DN' AND s.ship_no = g.ref_key
        LEFT JOIN bc_customer sc ON sc.id = s.customer_id
        LEFT JOIN sd_sales_invoice v ON g.mov_type = 'TR' AND v.dn_no = g.ref_key
        LEFT JOIN bc_customer vc ON vc.id = v.customer_id
        LEFT JOIN mm_assembly a ON g.mov_type IN ('BP', 'BI', 'DA', 'DR') AND a.assembly_no = g.ref_key
        """;

    private const string SelectMovementsOfLots = SelectMovements + """

        WHERE g.lot_id = ANY(@Ids)
        ORDER BY g.document_dt, g.id
        """;

    // A shipment's delivery, or a bill-first invoice's own issue (not a Credit Memo's), with the reversals of either. An issue
    // decreases stock; a reversal is known by its original. The invoice's arrival in the consignment warehouse is an increase
    // of the same document and is not an issue (MM-BR-357)
    private const string IssueFilter = """

        WHERE ((g.mov_type = 'DN' AND s.id IS NOT NULL) OR (g.mov_type = 'TR' AND v.id IS NOT NULL AND coalesce(v.type, 'IV') <> 'CM'))
          AND coalesce((SELECT p.qty FROM mm_goods_movement p WHERE p.id = g.parent_movement_id), g.qty) < 0
        """;

    private const string SelectIssuesByDocNo = SelectMovements + IssueFilter + """
          AND (g.ref_key = @DocNo
               OR (g.mov_type = 'DN' AND (g.ref_key, g.ref_seq) IN (
                   SELECT sh.ship_no, sl.seq
                   FROM sd_sales_invoice iv
                   JOIN sd_sales_invoice_line il ON il.sales_invoice_id = iv.id
                   JOIN sd_shipment_line sl ON sl.id = il.shipment_line_id
                   JOIN sd_shipment sh ON sh.id = sl.shipment_id
                   WHERE iv.dn_no = @DocNo)))
        ORDER BY g.document_dt, g.id
        """;

    private const string SelectIssuesByShipmentLine = SelectMovements + IssueFilter + """
          AND g.mov_type = 'DN'
          AND (g.ref_key, g.ref_seq) IN (SELECT sh.ship_no, sl.seq FROM sd_shipment_line sl
                                         JOIN sd_shipment sh ON sh.id = sl.shipment_id WHERE sl.id = @Id)
        ORDER BY g.document_dt, g.id
        """;

    // An invoice line of a shipped line goes back to that shipment line; a bill-first line has its own issue
    private const string SelectIssuesByInvoiceLine = SelectMovements + IssueFilter + """
          AND (g.ref_key, g.ref_seq) IN (
              SELECT sh.ship_no, sl.seq FROM sd_sales_invoice_line il
              JOIN sd_shipment_line sl ON sl.id = il.shipment_line_id
              JOIN sd_shipment sh ON sh.id = sl.shipment_id
              WHERE il.id = @Id
              UNION ALL
              SELECT iv.dn_no, il.seq FROM sd_sales_invoice_line il
              JOIN sd_sales_invoice iv ON iv.id = il.sales_invoice_id
              WHERE il.id = @Id AND il.shipment_line_id IS NULL)
        ORDER BY g.document_dt, g.id
        """;

    private const string SelectIssuesByCustomer = SelectMovements + IssueFilter + """
          AND ((g.mov_type = 'DN' AND s.customer_id = @CustomerId AND s.ship_dt BETWEEN @From AND @To)
               OR (g.mov_type = 'TR' AND v.customer_id = @CustomerId AND v.dn_dt BETWEEN @From AND @To))
        ORDER BY g.document_dt, g.id
        """;

    // Links of voided assemblies are kept with the assembly but no longer join the Lots (MM-BR-601)
    private const string SelectLinks = """
        SELECT k.set_lot_id, k.component_lot_id, a.assembly_no, a.assembly_dt, k.qty
        FROM mm_lot_link k
        JOIN mm_assembly a ON a.id = k.assembly_id
        WHERE a.status = 'OP' AND (k.set_lot_id = ANY(@Ids) OR k.component_lot_id = ANY(@Ids))
        ORDER BY a.assembly_dt, a.id, k.id
        """;

    public async Task<LotTraceLot?> FindLotAsync(long itemId, string lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<LotTraceLot>(
            new CommandDefinition(SelectLot, new { ItemId = itemId, LotNo = lotNo }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<LotTraceLot>> FindLotsAsync(
        IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<LotTraceLot>(
            new CommandDefinition(SelectLotsById, new { Ids = lotIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public async Task<IReadOnlyList<LotTraceMovement>> FindMovementsAsync(
        IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<LotTraceMovement>(
            new CommandDefinition(SelectMovementsOfLots, new { Ids = lotIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public async Task<IReadOnlyList<LotTraceLink>> FindLinksAsync(
        IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<LotTraceLink>(
            new CommandDefinition(SelectLinks, new { Ids = lotIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public async Task<IReadOnlyList<LotTraceMovement>> FindIssuesAsync(
        LotTraceIssueSource source, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var (sql, parameters) = source switch
        {
            { DocNo: { } docNo } => (SelectIssuesByDocNo, (object)new { DocNo = docNo }),
            { ShipmentLineId: { } id } => (SelectIssuesByShipmentLine, new { Id = id }),
            { InvoiceLineId: { } id } => (SelectIssuesByInvoiceLine, new { Id = id }),
            { CustomerId: { } customerId, From: { } from, To: { } to } =>
                (SelectIssuesByCustomer, new { CustomerId = customerId, From = from, To = to }),
            _ => throw new ArgumentException("The issue source names nothing.", nameof(source)),
        };
        return [.. await transaction.Connection!.QueryAsync<LotTraceMovement>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken))];
    }
}

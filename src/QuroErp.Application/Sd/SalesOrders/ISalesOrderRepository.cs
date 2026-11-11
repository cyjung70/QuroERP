using System.Data;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Application.Sd.SalesOrders;

/// <summary>sd_sales_order row with the customer name and discount status.</summary>
public sealed record SalesOrderRow(
    long Id, string SoNo, string Status, long CustomerId, string? CustomerName, DateOnly SoDt, DateOnly? ExpShipDt, DateOnly? DueDt,
    string CurrencyCode, decimal ExchangeRate, string BillingFlow, string? Rep, string? Terms, string? ShipVia, SalesOrderShipTo ShipTo,
    string? Remarks, string? DcStatus, decimal SupplyAmt, decimal TaxAmt, decimal TotSoAmt, decimal TotBaseAmt, DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt, string? CreatedBy, string? UpdatedBy);

/// <summary>sd_sales_order_line row with the item code, unit and standard price, and the latest invoice of the line.</summary>
public sealed record SalesOrderLineRow(
    long Id, int Seq, long ItemId, string? ItemCd, string? ItemName, decimal SoQty, decimal SoPrc, decimal SoAmt, string? DcCd,
    decimal DcRate, decimal? StdPrc, long TaxCodeId, decimal SupplyAmt, decimal TaxAmt, decimal ShipQty, decimal DnQty, string? InvNo,
    string? Unit);

/// <summary>An item for line defaults and line files: type, use flag, tax flags, standard sales price and price-level price.</summary>
public sealed record SalesOrderItemRow(
    long Id, string ItemCd, string? ItemName, string? ItemType, string? Unit, string? ValidFlag, bool Taxable, string VatKind,
    decimal? StdPrc, decimal? LevelPrc, string? UpcCode);

/// <summary>A sales order to insert or update: header fields, computed totals and lines.</summary>
public sealed record SalesOrderWrite(SalesOrderHeader Header, SalesOrderTotals Totals, IReadOnlyList<SalesOrderLine> Lines);

/// <summary>One set component line of the set component list: the set line, the component and its quantity per set.</summary>
public sealed record SalesOrderSetComponentRow(
    int Seq, string? SetItemCd, string? SetItemName, decimal SoQty, string? ComponentCd, string? ComponentName, string? Unit,
    string? Location, decimal ChildQty);

/// <summary>Item location of a line for the picking list.</summary>
public sealed record SalesOrderPickingRow(int Seq, string? ItemCd, string? ItemName, string? Unit, string? Location, decimal SoQty);

/// <summary>Sales orders (SD-UC-002). Callers pass their own transaction.</summary>
public interface ISalesOrderRepository
{
    /// <summary>Newest first (so_dt, so_no), at most <paramref name="limit"/> rows.</summary>
    Task<IReadOnlyList<SalesOrderRow>> SearchAsync(SalesOrderSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<SalesOrderRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<SalesOrderLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items with these ids that exist, with their price in <paramref name="priceLevel"/> (null: no level price).</summary>
    Task<IReadOnlyList<SalesOrderItemRow>> FindItemsByIdAsync(
        IReadOnlyCollection<long> itemIds, string? priceLevel, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items with these codes (item_cd) or, when <paramref name="byBarcode"/>, these barcodes (upc_code).</summary>
    Task<IReadOnlyList<SalesOrderItemRow>> FindItemsByKeyAsync(
        IReadOnlyCollection<string> keys, bool byBarcode, string? priceLevel, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>sd_discount_rate: rate by discount code.</summary>
    Task<IReadOnlyDictionary<string, decimal>> FindDiscountRatesAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The first tax code in use of this tax kind (SALES_TAX_EXEMPT, VAT_EXEMPT), or null.</summary>
    Task<long?> FindTaxCodeIdOfKindAsync(string taxKind, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A shipment or invoice of the order exists that is not void (SD-BR-035).</summary>
    Task<bool> HasShipmentOrInvoiceAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Copies the ship-to and freight to the customer (SD-BR-009).</summary>
    Task UpdateCustomerShipToAsync(long customerId, SalesOrderShipTo shipTo, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds or changes the prices of the price level (bc_sales_price, SD-BR-010).</summary>
    Task UpsertPriceLevelAsync(
        string priceLevel, IReadOnlyList<PriceLevelChange> prices, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new id.</returns>
    /// <param name="businessPlaceId">bc_business_place.id of the order (BC-BR-321). It does not change after creation.</param>
    Task<long> InsertAsync(
        string soNo, long businessPlaceId, SalesOrderWrite order, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Replaces the header and the lines: lines with an Id are updated, others added, missing ones deleted.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/> or a line id is not of this order.</returns>
    Task<bool> UpdateAsync(
        long id, SalesOrderWrite order, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the order and its lines.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines with the item location, in seq order (picking list).</summary>
    Task<IReadOnlyList<SalesOrderPickingRow>> FindPickingAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The components of the Set lines (sd_bom), in line seq and component seq order.</summary>
    Task<IReadOnlyList<SalesOrderSetComponentRow>> FindSetComponentsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);
}

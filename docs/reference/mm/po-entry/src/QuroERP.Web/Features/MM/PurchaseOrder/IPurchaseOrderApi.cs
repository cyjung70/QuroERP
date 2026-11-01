namespace QuroERP.Web.Features.MM.PurchaseOrder;

/// <summary>
/// REST client for /api/mm/purchase-orders (ASP.NET Core + Dapper + PostgreSQL behind it).
/// Every write returns the full document so the screen re-binds to server truth.
/// </summary>
public interface IPurchaseOrderApi
{
    // GET  /api/mm/purchase-orders/{poNo}
    Task<PoDocument?> GetAsync(string poNo, CancellationToken ct = default);

    // POST /api/mm/purchase-orders            (PoNo null → server assigns "PO" + YYMM + 5-digit seq)
    // PUT  /api/mm/purchase-orders/{poNo}     (lines carry RowState; MarkedForDelete rows are deleted)
    Task<PoDocument> SaveAsync(PoDocument doc, CancellationToken ct = default);

    // DELETE /api/mm/purchase-orders/{poNo}?rowVersion=n
    Task DeleteAsync(string poNo, long rowVersion, CancellationToken ct = default);

    // POST /api/mm/purchase-orders/{poNo}/approve | /close | /cancel
    Task<PoDocument> ChangeStatusAsync(string poNo, PoStatusCommand command, long rowVersion, CancellationToken ct = default);

    // GET /api/mm/purchase-orders/{poNo}/pdf  (FastReport.NET, layout based on AS IS po.jrxml)
    Task<byte[]> GetPdfAsync(string poNo, CancellationToken ct = default);

    // Lookups for popups
    Task<IReadOnlyList<VendorLookup>> SearchVendorsAsync(string? code, string? name, CancellationToken ct = default);
    Task<IReadOnlyList<ItemLookup>> SearchItemsAsync(string? code, string? name, string? itemClass, CancellationToken ct = default);
    Task<CurrencyPrecision> GetPrecisionAsync(string currency, CancellationToken ct = default);
    Task<decimal> GetTaxRateAsync(string taxCode, CancellationToken ct = default);
}

public enum PoStatusCommand { Approve, Close, Cancel }

public sealed record VendorLookup(string Code, string Name, string? Currency, string? PaymentTerms, string? Incoterms);
public sealed record ItemLookup(string Code, string Name, string ItemClass, string Unit, decimal LastPrice, decimal OnHand, string? DefaultTaxCode);

using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>
/// Sales order header on the sales order screen (SD-UC-002). <see cref="SalesOrderDto"/> is immutable, so the frame binds to this.
/// The customer is picked by name; the page keeps its id.
/// </summary>
public sealed class SalesOrderHeader
{
    public long Id { get; set; }
    public string? SoNo { get; set; }
    public string? Status { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateOnly? SoDt { get; set; }
    public DateOnly? ExpShipDt { get; set; }
    public DateOnly? DueDt { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? BillingFlow { get; set; }
    public string? Rep { get; set; }
    public string? Terms { get; set; }
    public string? ShipVia { get; set; }
    public string? FreightCharge { get; set; }
    public string? ShipName { get; set; }
    public string? ShipAddr1 { get; set; }
    public string? ShipAddr2 { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipZipCode { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipPhone { get; set; }
    public string? ShipFax { get; set; }
    public string? Remarks { get; set; }
    public string? DcStatus { get; set; }
    public decimal TotSoAmt { get; set; }

    /// <summary>A shipment or invoice exists, so the billing flow cannot change (SD-BR-035).</summary>
    public bool BillingFlowLocked { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>A saved order keeps its customer (SD-BR-023).</summary>
    public bool IsSaved => Id != 0;

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt, null, null);

    /// <summary>A row of the find dialog: number, date, customer, status and total.</summary>
    public static SalesOrderHeader From(SalesOrderSummaryDto order) => new()
    {
        Id = order.Id,
        SoNo = order.SoNo,
        Status = order.Status,
        CustomerId = order.CustomerId,
        CustomerName = order.CustomerName,
        SoDt = order.SoDt,
        CurrencyCode = order.CurrencyCode,
        TotSoAmt = order.TotSoAmt,
    };

    public static SalesOrderHeader From(SalesOrderDto order) => new()
    {
        Id = order.Id,
        SoNo = order.SoNo,
        Status = order.Status,
        CustomerId = order.CustomerId,
        CustomerName = order.CustomerName,
        SoDt = order.SoDt,
        ExpShipDt = order.ExpShipDt,
        DueDt = order.DueDt,
        CurrencyCode = order.CurrencyCode,
        ExchangeRate = order.ExchangeRate,
        BillingFlow = order.BillingFlow,
        BillingFlowLocked = order.BillingFlowLocked,
        Rep = order.Rep,
        Terms = order.Terms,
        ShipVia = order.ShipVia,
        FreightCharge = order.FreightCharge,
        ShipName = order.ShipName,
        ShipAddr1 = order.ShipAddr1,
        ShipAddr2 = order.ShipAddr2,
        ShipCity = order.ShipCity,
        ShipState = order.ShipState,
        ShipZipCode = order.ShipZipCode,
        ShipCountry = order.ShipCountry,
        ShipPhone = order.ShipPhone,
        ShipFax = order.ShipFax,
        Remarks = order.Remarks,
        DcStatus = order.DcStatus,
        TotSoAmt = order.TotSoAmt,
        UpdatedAt = order.UpdatedAt,
        CreatedAt = order.CreatedAt,
        CreatedBy = order.CreatedBy,
        UpdatedBy = order.UpdatedBy,
    };

    /// <summary>The save request with the kept lines in screen order (lines left out are deleted, SD-UC-002).</summary>
    /// <param name="answers">The answers to SD-W-009, SD-W-010 and SD-W-309 so far; null lets the API ask.</param>
    public SaveSalesOrderRequest ToRequest(bool isNew, IEnumerable<SalesOrderLine> lines, SalesOrderSaveAnswers answers) =>
        new(
            CustomerId ?? 0,
            SoDt ?? DateOnly.FromDateTime(DateTime.Today),
            ExpShipDt,
            DueDt,
            CurrencyCode,
            ExchangeRate,
            Clean(BillingFlow),
            Clean(Rep),
            Clean(Terms),
            Clean(ShipVia),
            Clean(FreightCharge),
            Clean(ShipName),
            Clean(ShipAddr1),
            Clean(ShipAddr2),
            Clean(ShipCity),
            Clean(ShipState),
            Clean(ShipZipCode),
            Clean(ShipCountry),
            Clean(ShipPhone),
            Clean(ShipFax),
            Clean(Remarks),
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            answers.UpdateCustomerShipTo,
            answers.UpdatePriceLevel,
            answers.ConfirmCreditLimit);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The answers to the save questions (SD-BR-009, SD-BR-010, SD-BR-309); null is not asked yet.</summary>
public sealed record SalesOrderSaveAnswers(bool? UpdateCustomerShipTo = null, bool? UpdatePriceLevel = null, bool? ConfirmCreditLimit = null)
{
    public const string ShipToField = "updateCustomerShipTo";
    public const string PriceLevelField = "updatePriceLevel";
    public const string CreditLimitField = "confirmCreditLimit";

    /// <summary>The question the API asked is not answered yet.</summary>
    public bool Asks(string? confirmField) => confirmField switch
    {
        ShipToField => UpdateCustomerShipTo is null,
        PriceLevelField => UpdatePriceLevel is null,
        CreditLimitField => ConfirmCreditLimit is null,
        _ => false,
    };

    public SalesOrderSaveAnswers With(string confirmField, bool answer) => confirmField switch
    {
        ShipToField => this with { UpdateCustomerShipTo = answer },
        PriceLevelField => this with { UpdatePriceLevel = answer },
        CreditLimitField => this with { ConfirmCreditLimit = answer },
        _ => this,
    };
}

/// <summary>A line of the sales order grid (sd.sales-order.entry). Codes are shown; the ids are hidden columns.</summary>
public sealed class SalesOrderLine
{
    public long Id { get; set; }
    public int? Seq { get; set; }
    public long? ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public decimal? SoQty { get; set; }
    public decimal? SoPrc { get; set; }
    public decimal? SoAmt { get; set; }
    public string? DcCd { get; set; }
    public decimal? DcRate { get; set; }
    public decimal? StdPrc { get; set; }
    public long? TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmt { get; set; }
    public decimal ShipQty { get; set; }
    public decimal DnQty { get; set; }
    public string? InvNo { get; set; }

    /// <summary>A shipped or invoiced line is locked in every cell and cannot be removed (SD-BR-003).</summary>
    public bool IsShippedOrInvoiced() => ShipQty > 0 || DnQty > 0;

    public static SalesOrderLine From(SalesOrderLineDto line, IReadOnlyDictionary<long, string> taxCodes) => new()
    {
        Id = line.Id,
        Seq = line.Seq,
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        SoQty = line.SoQty,
        SoPrc = line.SoPrc,
        SoAmt = line.SoAmt,
        DcCd = line.DcCd,
        DcRate = line.DcRate,
        StdPrc = line.StdPrc,
        TaxCodeId = line.TaxCodeId,
        TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
        TaxAmt = line.TaxAmt,
        ShipQty = line.ShipQty,
        DnQty = line.DnQty,
        InvNo = line.InvNo,
    };

    /// <summary>A new line from the line defaults or a line file row (SD-BR-016, 020, 033, 401, 405).</summary>
    public static SalesOrderLine From(SalesOrderLineDefaultsDto defaults, IReadOnlyDictionary<long, string> taxCodes) => new()
    {
        ItemId = defaults.ItemId,
        ItemCd = defaults.ItemCd,
        ItemName = defaults.ItemName,
        Unit = defaults.Unit,
        SoQty = defaults.SoQty,
        SoPrc = defaults.SoPrc,
        StdPrc = defaults.StdPrc,
        DcCd = defaults.DcCd,
        DcRate = defaults.DcRate,
        TaxCodeId = defaults.TaxCodeId,
        TaxCode = defaults.TaxCodeId is { } id ? taxCodes.GetValueOrDefault(id) : null,
    };

    /// <summary>An empty discount code is sent as "" (no discount), so the API does not put the customer's back (SD-BR-405).</summary>
    public SaveSalesOrderLineRequest ToRequest() =>
        new(Id == 0 ? null : Id, ItemId ?? 0, ItemName, SoQty, SoPrc, DcCd?.Trim() ?? "", TaxCodeId);
}

/// <summary>Sales order statuses (SD-BR-012) and their chips. Only SO can be changed or deleted (SD-BR-003).</summary>
public static class SalesOrderStatus
{
    public const string Open = "SO";
    public const string PartlyShipped = "RE";
    public const string Closed = "CL";

    public static ChipKind ChipOf(string? status) => status switch
    {
        Open => ChipKind.Brand,
        PartlyShipped => ChipKind.Warn,
        Closed => ChipKind.Ok,
        _ => ChipKind.Neutral,
    };

    /// <summary>A new order, or a stored one in SO.</summary>
    public static bool CanSave(SalesOrderHeader order) => order.Status is null or Open;

    /// <summary>A stored SO order without a shipment or invoice.</summary>
    public static bool CanDelete(SalesOrderHeader order) => order.Status == Open && !order.BillingFlowLocked;
}

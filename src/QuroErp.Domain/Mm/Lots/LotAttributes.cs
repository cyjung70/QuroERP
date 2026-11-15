namespace QuroErp.Domain.Mm.Lots;

/// <summary>What a purchase receipt line tells about its Lot (mm_lot, MM-BR-364): the supplier's Lot number and the dates.</summary>
public sealed record LotAttributes(string? SupplierLotNo, DateOnly? MfgDt, DateOnly? ExpiryDt)
{
    public static LotAttributes None { get; } = new(null, null, null);

    public bool IsEmpty => SupplierLotNo is null && MfgDt is null && ExpiryDt is null;
}

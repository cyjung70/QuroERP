using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// One mm_goods_movement row. The ledger is never updated or deleted: a change or cancel adds a reversal with the
/// same type and the opposite sign that points to the original (MM-BR-601, MM-BR-606).
/// </summary>
public sealed class GoodsMovement
{
    /// <summary>Invalid movement: zero qty, wrong sign for the type, or a reversal that is not allowed.</summary>
    public const string InvalidMovement = "MM-E-606";

    private GoodsMovement(
        string movType, DateOnly documentDt, long itemId, long warehouseId, decimal qty, decimal prc,
        string? refKey, int? refSeq, long? parentMovementId, long? setItemId, decimal amount)
    {
        MovType = movType;
        DocumentDt = documentDt;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Qty = qty;
        Prc = prc;
        Amount = amount;
        RefKey = refKey;
        RefSeq = refSeq;
        ParentMovementId = parentMovementId;
        SetItemId = setItemId;
    }

    public long Id { get; set; }

    /// <summary>Movement number, assigned when the movement is saved. A reversal gets its own number.</summary>
    public string DocumentNo { get; set; } = "";

    public DateOnly DocumentDt { get; }

    public string MovType { get; }

    public long ItemId { get; }

    public long WarehouseId { get; }

    /// <summary>Stock change: + increases, − decreases. Never 0, except a price difference (PV), which is always 0.</summary>
    public decimal Qty { get; }

    public decimal Prc { get; }

    /// <summary>Stock value change: qty × prc with the sign of qty, or the price difference of a PV (MM-BR-606).</summary>
    public decimal Amount { get; }

    public string? RefKey { get; }

    public int? RefSeq { get; }

    /// <summary>The original movement this reversal cancels; null for an original movement.</summary>
    public long? ParentMovementId { get; }

    /// <summary>The set item whose line was expanded into this component movement; null otherwise (MM-BR-007).</summary>
    public long? SetItemId { get; }

    /// <summary>
    /// The Location of the movement, set when it is posted: '*' unless the line names one (MM-BR-713). A reversal keeps the
    /// original Location.
    /// </summary>
    public long? LocationId { get; set; }

    /// <summary>
    /// The lot of the movement, set when it is posted: '*' for an item without lot management (MM-BR-363). A reversal keeps the
    /// original lot (MM-BR-355).
    /// </summary>
    public long? LotId { get; set; }

    /// <summary>The Pallet this movement moved as a whole; null for stock that is not a Pallet line (MM-BR-912, MM-BR-913).</summary>
    public long? PalletId { get; set; }

    /// <summary>This movement left the item + warehouse stock below zero (MM-BR-002).</summary>
    public bool NegativeStock { get; set; }

    /// <summary>
    /// An original movement. Its qty sign must match the type (MM-BR-606). A price difference (PV) has qty 0, price 0 and only
    /// the amount, which must not be 0; every other type takes qty × prc as its amount.
    /// </summary>
    /// <exception cref="DomainException">MM-E-606 for an unknown type, zero qty, the wrong sign or a PV without an amount.</exception>
    public static GoodsMovement Create(
        string movType, DateOnly documentDt, long itemId, long warehouseId, decimal qty, decimal prc,
        string? refKey = null, int? refSeq = null, long? setItemId = null, decimal? amount = null)
    {
        if (!MovementType.IsKnown(movType))
        {
            throw new DomainException(InvalidMovement, movType, qty);
        }
        if (MovementType.IsAmountOnly(movType))
        {
            if (qty != 0 || amount is null or 0)
            {
                throw new DomainException(InvalidMovement, movType, qty);
            }
            return new GoodsMovement(movType, documentDt, itemId, warehouseId, 0m, 0m, refKey, refSeq, null, setItemId, amount.Value);
        }
        if (qty == 0)
        {
            throw new DomainException(InvalidMovement, movType, qty);
        }
        var sign = MovementType.OriginalSign(movType);
        if (sign != 0 && Math.Sign(qty) != sign)
        {
            throw new DomainException(InvalidMovement, movType, qty);
        }
        return new GoodsMovement(movType, documentDt, itemId, warehouseId, qty, prc, refKey, refSeq, null, setItemId, qty * prc);
    }

    /// <summary>Rebuilds a saved row. Without the stored amount it is qty × prc.</summary>
    public static GoodsMovement Restore(
        long id, string documentNo, string movType, DateOnly documentDt, long itemId, long warehouseId, decimal qty, decimal prc,
        string? refKey, int? refSeq, long? parentMovementId, bool negativeStock, long? setItemId = null, long? lotId = null,
        decimal? amount = null, long? locationId = null, long? palletId = null) =>
        new(movType, documentDt, itemId, warehouseId, qty, prc, refKey, refSeq, parentMovementId, setItemId, amount ?? qty * prc)
        {
            Id = id,
            DocumentNo = documentNo,
            NegativeStock = negativeStock,
            LotId = lotId,
            LocationId = locationId,
            PalletId = palletId,
        };

    /// <summary>
    /// The reversal of this movement on reversalDt: same item, warehouse, Location, lot, type, price, reference and set, opposite
    /// sign of qty and amount (MM-BR-601, MM-BR-355).
    /// </summary>
    /// <param name="alreadyReversed">Another movement already points to this one.</param>
    /// <exception cref="DomainException">MM-E-606 when this movement is itself a reversal or is already reversed.</exception>
    public GoodsMovement Reverse(DateOnly reversalDt, bool alreadyReversed)
    {
        if (ParentMovementId is not null || alreadyReversed)
        {
            throw new DomainException(InvalidMovement, MovType, -Qty);
        }
        return new GoodsMovement(MovType, reversalDt, ItemId, WarehouseId, -Qty, Prc, RefKey, RefSeq, Id, SetItemId, -Amount)
        {
            LocationId = LocationId,
            LotId = LotId,
            PalletId = PalletId,
        };
    }
}

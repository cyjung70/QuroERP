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
        string? refKey, int? refSeq, long? parentMovementId, long? setItemId)
    {
        MovType = movType;
        DocumentDt = documentDt;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Qty = qty;
        Prc = prc;
        Amount = qty * prc;
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

    /// <summary>Stock change: + increases, − decreases. Never 0.</summary>
    public decimal Qty { get; }

    public decimal Prc { get; }

    /// <summary>Stock value change, qty × prc, same sign as qty.</summary>
    public decimal Amount { get; }

    public string? RefKey { get; }

    public int? RefSeq { get; }

    /// <summary>The original movement this reversal cancels; null for an original movement.</summary>
    public long? ParentMovementId { get; }

    /// <summary>The set item whose line was expanded into this component movement; null otherwise (MM-BR-007).</summary>
    public long? SetItemId { get; }

    /// <summary>This movement left the item + warehouse stock below zero (MM-BR-002).</summary>
    public bool NegativeStock { get; set; }

    /// <summary>An original movement. Its qty sign must match the type (MM-BR-606).</summary>
    /// <exception cref="DomainException">MM-E-606 for an unknown type, zero qty or the wrong sign.</exception>
    public static GoodsMovement Create(
        string movType, DateOnly documentDt, long itemId, long warehouseId, decimal qty, decimal prc,
        string? refKey = null, int? refSeq = null, long? setItemId = null)
    {
        if (!MovementType.IsKnown(movType) || qty == 0)
        {
            throw new DomainException(InvalidMovement, movType, qty);
        }
        var sign = MovementType.OriginalSign(movType);
        if (sign != 0 && Math.Sign(qty) != sign)
        {
            throw new DomainException(InvalidMovement, movType, qty);
        }
        return new GoodsMovement(movType, documentDt, itemId, warehouseId, qty, prc, refKey, refSeq, null, setItemId);
    }

    /// <summary>Rebuilds a saved row.</summary>
    public static GoodsMovement Restore(
        long id, string documentNo, string movType, DateOnly documentDt, long itemId, long warehouseId, decimal qty, decimal prc,
        string? refKey, int? refSeq, long? parentMovementId, bool negativeStock, long? setItemId = null) =>
        new(movType, documentDt, itemId, warehouseId, qty, prc, refKey, refSeq, parentMovementId, setItemId)
        {
            Id = id,
            DocumentNo = documentNo,
            NegativeStock = negativeStock,
        };

    /// <summary>
    /// The reversal of this movement on reversalDt: same item, warehouse, type, price, reference and set, opposite sign
    /// (MM-BR-601).
    /// </summary>
    /// <param name="alreadyReversed">Another movement already points to this one.</param>
    /// <exception cref="DomainException">MM-E-606 when this movement is itself a reversal or is already reversed.</exception>
    public GoodsMovement Reverse(DateOnly reversalDt, bool alreadyReversed)
    {
        if (ParentMovementId is not null || alreadyReversed)
        {
            throw new DomainException(InvalidMovement, MovType, -Qty);
        }
        return new GoodsMovement(MovType, reversalDt, ItemId, WarehouseId, -Qty, Prc, RefKey, RefSeq, Id, SetItemId);
    }
}

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>What the Pallet rules read of a stored Pallet (MM-BR-911): its stock key, qty and status.</summary>
public sealed record PalletState(
    long Id, string PalletNo, long ItemId, long LotId, long WarehouseId, long LocationId, decimal Qty, string Status);

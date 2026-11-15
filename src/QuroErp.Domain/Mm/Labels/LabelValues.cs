using System.Globalization;

namespace QuroErp.Domain.Mm.Labels;

/// <summary>
/// A label of each type with the values its fields print (<see cref="LabelField"/>) and its DataMatrix content (MM-BR-918). Qty
/// prints without trailing zeros and the expiry as yyyy-MM-dd; the DataMatrix has its own formats (<see cref="LabelCode"/>).
/// </summary>
public static class LabelValues
{
    public static Label Pallet(long id, string palletNo, string itemCd, string? itemName, string lotNo, decimal qty, DateOnly? expiryDt) =>
        new(LabelType.Pallet, id,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [LabelField.PalletNo] = palletNo,
                [LabelField.ItemCd] = itemCd,
                [LabelField.ItemName] = itemName,
                [LabelField.LotNo] = lotNo,
                [LabelField.Qty] = qty.ToString("0.############", CultureInfo.InvariantCulture),
                [LabelField.ExpiryDt] = Date(expiryDt),
            },
            LabelCode.ForPallet(palletNo, itemCd, lotNo, qty, expiryDt));

    public static Label Item(long id, string itemCd, string? itemName) =>
        new(LabelType.Item, id,
            new Dictionary<string, string?>(StringComparer.Ordinal) { [LabelField.ItemCd] = itemCd, [LabelField.ItemName] = itemName },
            LabelCode.ForItem(itemCd));

    public static Label Lot(long id, string itemCd, string? itemName, string lotNo, DateOnly? expiryDt) =>
        new(LabelType.Lot, id,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [LabelField.ItemCd] = itemCd,
                [LabelField.ItemName] = itemName,
                [LabelField.LotNo] = lotNo,
                [LabelField.ExpiryDt] = Date(expiryDt),
            },
            LabelCode.ForLot(itemCd, lotNo));

    public static Label Location(long id, string warehouseCode, string locationCode) =>
        new(LabelType.Location, id,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [LabelField.WarehouseCode] = warehouseCode,
                [LabelField.LocationCode] = locationCode,
            },
            LabelCode.ForLocation(warehouseCode, locationCode));

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

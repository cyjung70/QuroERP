using System.ComponentModel.DataAnnotations;

namespace QuroErp.Contracts.Mm.Inventory;

/// <summary>Inventory setting API route (MM-BR-003, MM-BR-102, MM-BR-420): GET, PUT api/mm/inventory-setting. There is only one setting.</summary>
public static class InventorySettingRoutes
{
    public const string Base = "api/mm/inventory-setting";
}

/// <summary>Values of NegativeStockMode (inventory.md "마이너스 재고"). Resource key: Mm.InventorySetting.Mode.{value}.</summary>
public static class NegativeStockModes
{
    public const string AllowInMonth = "ALLOW_IN_MONTH";
    public const string Deny = "DENY";

    public static IReadOnlyList<string> All { get; } = [AllowInMonth, Deny];
}

/// <summary>Values of OverReceiptMode (MM-BR-102). Resource key: Mm.InventorySetting.OverReceiptMode.{value}.</summary>
public static class OverReceiptModes
{
    public const string Deny = "DENY";
    public const string Warn = "WARN";

    public static IReadOnlyList<string> All { get; } = [Deny, Warn];
}

/// <param name="NegativeStockMode">ALLOW_IN_MONTH or DENY.</param>
/// <param name="OverReceiptMode">DENY or WARN.</param>
/// <param name="AllowCostAdjustmentEdit">Whether a DRAFT cost adjustment line may be edited (MM-BR-420).</param>
/// <param name="UpdatedAt">Send it back on PUT to detect concurrent updates.</param>
public sealed record InventorySettingDto(
    string NegativeStockMode, string OverReceiptMode, bool AllowCostAdjustmentEdit, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <param name="AllowCostAdjustmentEdit">Whether a DRAFT cost adjustment line may be edited (MM-BR-420).</param>
/// <param name="UpdatedAt">The UpdatedAt from the last GET. A different value means someone else saved first (409).</param>
public sealed record UpdateInventorySettingRequest(
    [Required, AllowedValues(NegativeStockModes.AllowInMonth, NegativeStockModes.Deny)]
    string NegativeStockMode,
    [Required, AllowedValues(OverReceiptModes.Deny, OverReceiptModes.Warn)]
    string OverReceiptMode,
    bool AllowCostAdjustmentEdit,
    DateTimeOffset UpdatedAt);

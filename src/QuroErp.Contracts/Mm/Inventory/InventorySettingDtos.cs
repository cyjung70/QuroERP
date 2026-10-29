using System.ComponentModel.DataAnnotations;

namespace QuroErp.Contracts.Mm.Inventory;

/// <summary>Inventory setting API route (MM-BR-003): GET, PUT api/mm/inventory-setting. There is only one setting.</summary>
public static class InventorySettingRoutes
{
    public const string Base = "api/mm/inventory-setting";
}

/// <summary>Values of NegativeStockMode (inventory.md "마이너스 재고"). Resource key: Mm.InventorySetting.Mode.{value}.</summary>
public static class NegativeStockModes
{
    public const string Allow = "ALLOW";
    public const string AllowInMonth = "ALLOW_IN_MONTH";
    public const string Deny = "DENY";

    public static IReadOnlyList<string> All { get; } = [Allow, AllowInMonth, Deny];
}

/// <param name="NegativeStockMode">ALLOW, ALLOW_IN_MONTH or DENY.</param>
/// <param name="UpdatedAt">Send it back on PUT to detect concurrent updates.</param>
public sealed record InventorySettingDto(string NegativeStockMode, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <param name="UpdatedAt">The UpdatedAt from the last GET. A different value means someone else saved first (409).</param>
public sealed record UpdateInventorySettingRequest(
    [Required, AllowedValues(NegativeStockModes.Allow, NegativeStockModes.AllowInMonth, NegativeStockModes.Deny)]
    string NegativeStockMode,
    DateTimeOffset UpdatedAt);

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// Whether stock may go below zero (inventory.md, ADR-0025). One global setting (mm_inventory_setting).
/// Stored as the string code (ALLOW, ALLOW_IN_MONTH, DENY).
/// </summary>
public enum NegativeStockMode
{
    /// <summary>Always allowed. Movements that go negative are flagged (mm_goods_movement.negative_stock).</summary>
    Allow,

    /// <summary>Allowed during the month; month closing is blocked while any item ends the month negative (MM-BR-407).</summary>
    AllowInMonth,

    /// <summary>Not allowed (default). Issuing more than on-hand is blocked (MM-BR-001).</summary>
    Deny,
}

/// <summary>String codes of NegativeStockMode stored in mm_inventory_setting.negative_stock_mode.</summary>
public static class NegativeStockModeCode
{
    public const string Allow = "ALLOW";
    public const string AllowInMonth = "ALLOW_IN_MONTH";
    public const string Deny = "DENY";

    public static string ToCode(this NegativeStockMode mode) => mode switch
    {
        NegativeStockMode.Allow => Allow,
        NegativeStockMode.AllowInMonth => AllowInMonth,
        NegativeStockMode.Deny => Deny,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <returns>false when code is not one of the three codes.</returns>
    public static bool TryParse(string? code, out NegativeStockMode mode)
    {
        (var ok, mode) = code switch
        {
            Allow => (true, NegativeStockMode.Allow),
            AllowInMonth => (true, NegativeStockMode.AllowInMonth),
            Deny => (true, NegativeStockMode.Deny),
            _ => (false, NegativeStockMode.Deny),
        };
        return ok;
    }
}

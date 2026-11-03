using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Bc.SalesPrices;

/// <summary>The item fields a sales price row needs (SD-BR-408).</summary>
public sealed record SalesPriceItem(long Id, string ItemCd, string? ItemType, string? ValidFlag);

/// <summary>
/// Sales price table rules (BC-UC-014, docs/business-rules/SD/pricing.md). One price per (price level, item); the
/// price level is the customer master's price level. Prices are in the company base currency like the item standard price.
/// </summary>
public static class SalesPriceRules
{
    public const int LevelMaxLength = 5;

    /// <summary>Price level longer than 5 characters, or an item twice in the level. Args: price level, item code.</summary>
    public const string KeyCode = "SD-E-403";

    /// <summary>The item does not exist, is not an inventory item, or is not in use. Arg: item code.</summary>
    public const string ItemCode = "SD-E-408";

    /// <summary>The price is not above zero. Arg: item code.</summary>
    public const string PriceCode = "SD-E-407";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The price level as stored: trimmed, upper case, 1 to 5 characters (SD-BR-403).</summary>
    /// <exception cref="DomainException">Common-E-005 when empty, SD-E-403 when longer than 5 characters.</exception>
    public static string NormalizeLevel(string? priceLevel)
    {
        var level = priceLevel?.Trim().ToUpperInvariant() ?? "";
        if (level.Length == 0)
        {
            throw new DomainException(RequiredCode, "priceLevel");
        }
        return level.Length <= LevelMaxLength ? level : throw new DomainException(KeyCode, level, "-");
    }

    /// <summary>SD-BR-408: an inventory item in use (MM-BR-809).</summary>
    /// <param name="item">The item with that code, or null when there is none.</param>
    /// <exception cref="DomainException">SD-E-408.</exception>
    public static void EnsureItem(string itemCd, SalesPriceItem? item)
    {
        if (item is null || item.ItemType != ItemStock.Inventory || item.ValidFlag == Item.ValidFlagNo)
        {
            throw new DomainException(ItemCode, itemCd);
        }
    }

    /// <summary>SD-BR-407: above zero, with no more decimals than the SD unit price of the company country (BC-BR-423, BC-BR-429).</summary>
    /// <exception cref="DomainException">SD-E-407; BC-E-423 for too many decimals.</exception>
    public static void EnsurePrice(string itemCd, decimal salesPrc, IDecimalPolicy decimals)
    {
        if (salesPrc <= 0)
        {
            throw new DomainException(PriceCode, itemCd);
        }
        decimals.EnsureUnitPrice(DecimalModule.Sd, currencyCode: null, salesPrc, "salesPrc");
    }

    /// <summary>SD-BR-403: each item once in the level after the save.</summary>
    /// <param name="itemCds">Item codes of the rows the level keeps: stored rows not deleted or changed, plus changed and added rows.</param>
    /// <exception cref="DomainException">SD-E-403 with the first repeated item.</exception>
    public static void EnsureUnique(string level, IEnumerable<string> itemCds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemCd in itemCds)
        {
            if (!seen.Add(itemCd))
            {
                throw new DomainException(KeyCode, level, itemCd);
            }
        }
    }
}

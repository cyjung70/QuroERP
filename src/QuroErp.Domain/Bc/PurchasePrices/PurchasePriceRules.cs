using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Bc.PurchasePrices;

/// <summary>The item fields a purchase price row needs (MM-BR-902).</summary>
public sealed record PurchasePriceItem(long Id, string ItemCd, string? ItemType, string? ValidFlag);

/// <summary>
/// Purchase price table rules (BC-UC-015, docs/business-rules/MM/purchase-price.md). One price per (price group, item); the
/// price group is the vendor master's group value. Prices are in the company base currency like the item standard price.
/// </summary>
public static class PurchasePriceRules
{
    public const int GroupMaxLength = 5;

    /// <summary>Price group longer than 5 characters, or an item twice in the group. Args: price group, item code.</summary>
    public const string KeyCode = "MM-E-901";

    /// <summary>The item does not exist, is not an inventory item, or is not in use. Arg: item code.</summary>
    public const string ItemCode = "MM-E-902";

    /// <summary>The price is not above zero. Arg: item code.</summary>
    public const string PriceCode = "MM-E-903";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The price group as stored: trimmed, upper case, 1 to 5 characters (MM-BR-901).</summary>
    /// <exception cref="DomainException">Common-E-005 when empty, MM-E-901 when longer than 5 characters.</exception>
    public static string NormalizeGroup(string? vendorPrice)
    {
        var group = vendorPrice?.Trim().ToUpperInvariant() ?? "";
        if (group.Length == 0)
        {
            throw new DomainException(RequiredCode, "vendorPrice");
        }
        return group.Length <= GroupMaxLength ? group : throw new DomainException(KeyCode, group, "-");
    }

    /// <summary>MM-BR-902: an inventory item in use (MM-BR-809).</summary>
    /// <param name="item">The item with that code, or null when there is none.</param>
    /// <exception cref="DomainException">MM-E-902.</exception>
    public static void EnsureItem(string itemCd, PurchasePriceItem? item)
    {
        if (item is null || item.ItemType != ItemStock.Inventory || item.ValidFlag == Item.ValidFlagNo)
        {
            throw new DomainException(ItemCode, itemCd);
        }
    }

    /// <summary>MM-BR-903: above zero, with no more decimals than the MM unit price of the company country (BC-BR-423, BC-BR-429).</summary>
    /// <exception cref="DomainException">MM-E-903; BC-E-423 for too many decimals.</exception>
    public static void EnsurePrice(string itemCd, decimal purchasePrc, IDecimalPolicy decimals)
    {
        if (purchasePrc <= 0)
        {
            throw new DomainException(PriceCode, itemCd);
        }
        decimals.EnsureUnitPrice(DecimalModule.Mm, currencyCode: null, purchasePrc, "purchasePrc");
    }

    /// <summary>MM-BR-901: each item once in the group after the save.</summary>
    /// <param name="itemCds">Item codes of the rows the group keeps: stored rows not deleted or changed, plus changed and added rows.</param>
    /// <exception cref="DomainException">MM-E-901 with the first repeated item.</exception>
    public static void EnsureUnique(string group, IEnumerable<string> itemCds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemCd in itemCds)
        {
            if (!seen.Add(itemCd))
            {
                throw new DomainException(KeyCode, group, itemCd);
            }
        }
    }
}

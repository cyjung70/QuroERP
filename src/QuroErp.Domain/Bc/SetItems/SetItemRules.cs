using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Bc.SetItems;

/// <summary>The item fields a set or a component needs (SD-BR-501, SD-BR-502, MM-BR-361).</summary>
public sealed record SetItemCandidate(long Id, string ItemCd, string? ItemName, string? ItemType, string? ValidFlag, bool LotManaged);

/// <summary>
/// Set item composition rules (BC-UC-013, docs/business-rules/SD/set-item.md). A set (item type Set) has no stock of its own;
/// stock movements expand it into these components one level deep (MM-BR-007).
/// </summary>
public static class SetItemRules
{
    /// <summary>The set item does not exist, is not a Set, or is not in use. Arg: item code.</summary>
    public const string SetCode = "SD-E-501";

    /// <summary>The component does not exist, is not an Inventory item, or is not in use. Arg: item code.</summary>
    public const string ComponentCode = "SD-E-502";

    /// <summary>The component qty is not above zero. Arg: item code.</summary>
    public const string QtyCode = "SD-E-503";

    /// <summary>The same component twice in the set. Arg: item code.</summary>
    public const string DuplicateCode = "SD-E-504";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>An item code as stored: trimmed and upper case.</summary>
    /// <exception cref="DomainException">Common-E-005 when empty.</exception>
    public static string NormalizeCode(string? itemCd, string field)
    {
        var code = itemCd?.Trim().ToUpperInvariant() ?? "";
        return code.Length > 0 ? code : throw new DomainException(RequiredCode, field);
    }

    /// <summary>SD-BR-501: the set is in the item master, of type Set and in use.</summary>
    /// <param name="item">The item with that code, or null when there is none.</param>
    /// <exception cref="DomainException">SD-E-501.</exception>
    public static SetItemCandidate EnsureSet(string itemCd, SetItemCandidate? item) =>
        item is not null && item.ItemType == ItemStock.Set && item.ValidFlag != Item.ValidFlagNo
            ? item
            : throw new DomainException(SetCode, itemCd);

    /// <summary>
    /// SD-BR-502: a component is an Inventory item in use (MM-BR-809), so no Service and no set inside a set. MM-BR-361: not a
    /// lot-managed item, since a set issue has no way to pick component lots.
    /// </summary>
    /// <exception cref="DomainException">SD-E-502; MM-E-361 for a lot-managed item.</exception>
    public static SetItemCandidate EnsureComponent(string itemCd, SetItemCandidate? item)
    {
        if (item is null || item.ItemType != ItemStock.Inventory || item.ValidFlag == Item.ValidFlagNo)
        {
            throw new DomainException(ComponentCode, itemCd);
        }
        return item.LotManaged ? throw new DomainException(LotRules.SetComponentCode, itemCd) : item;
    }

    /// <summary>SD-BR-503: above zero, with no more decimals than the SD quantity setting (BC-BR-423). Expansion does not round (MM-BR-007).</summary>
    /// <exception cref="DomainException">SD-E-503; BC-E-423 for too many decimals.</exception>
    public static void EnsureQty(string itemCd, decimal qty, IDecimalPolicy decimals)
    {
        if (qty <= 0)
        {
            throw new DomainException(QtyCode, itemCd);
        }
        decimals.EnsureQty(DecimalModule.Sd, qty, "qty");
    }

    /// <summary>SD-BR-504: each component once in the set after the save.</summary>
    /// <param name="itemCds">Component codes the set keeps: stored rows not deleted or changed, plus changed and added rows.</param>
    /// <exception cref="DomainException">SD-E-504 with the first repeated component.</exception>
    public static void EnsureUnique(IEnumerable<string> itemCds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemCd in itemCds)
        {
            if (!seen.Add(itemCd))
            {
                throw new DomainException(DuplicateCode, itemCd);
            }
        }
    }

    /// <summary>SD-BR-505: new rows get the set's largest sequence + 1, + 2 and so on in the order given.</summary>
    /// <param name="maxSeq">The largest stored sequence of the set, 0 when it has none.</param>
    public static IReadOnlyList<int> NextSeqs(int maxSeq, int count) => [.. Enumerable.Range(maxSeq + 1, count)];
}

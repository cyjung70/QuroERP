using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Items;

/// <summary>
/// Values the item API may change (BC-UC-009). Moving average cost, stock quantity and stock value are not here:
/// only stock movements change them (MM-BR-804).
/// </summary>
public sealed record ItemFields(
    string? ItemName,
    string? ItemName2,
    string? ItemDesc,
    string? ItemType,
    string? Unit,
    string? ItemClass,
    string? ValidFlag,
    decimal? UsdPrc,
    decimal? SoUsdPrc,
    string? UpcCode,
    string? Location,
    decimal? ReorderPoint,
    string? Memo1,
    string? Memo2,
    string? Memo3,
    string? Memo4,
    string? Memo5,
    string? ItemGrade,
    bool? Taxable,
    string? VatKind);

/// <summary>Item master (bc_item) and its rules MM-BR-801~809, 825, 831, 832.</summary>
public sealed class Item
{
    /// <summary>Code missing, not upper case, too long, has spaces, or already used. Arg: item code.</summary>
    public const string InvalidCodeCode = "MM-E-801";

    /// <summary>Required value missing. Arg: property name.</summary>
    public const string RequiredCode = "MM-E-802";

    /// <summary>Price or reorder point below 0. Arg: property name.</summary>
    public const string NegativeCode = "MM-E-803";

    /// <summary>Type change of an item with stock, movements or set structure. Arg: item code.</summary>
    public const string TypeLockedCode = "MM-E-806";

    /// <summary>Delete of an item that is used anywhere. Arg: item code.</summary>
    public const string InUseCode = "MM-E-807";

    /// <summary>Item not in use picked for a new document line, price or set structure. Arg: item code.</summary>
    public const string NotInUseCode = "MM-E-809";

    /// <summary>Text too long. Args: property name, maximum length.</summary>
    public const string TooLongCode = "MM-E-825";

    /// <summary>Unknown KR VAT kind. Arg: the value.</summary>
    public const string InvalidVatKindCode = "MM-E-831";

    public const int ItemCdMaxLength = 18;

    public const string DefaultItemType = "INVENTORY";
    public const string DefaultUnit = "EA";
    public const string ValidFlagYes = "Y";
    public const string ValidFlagNo = "N";

    /// <summary>New purchase price default (MM-BR-803).</summary>
    public const decimal DefaultUsdPrc = 0.01m;

    public const string VatTaxable = "TAXABLE";
    public const string VatExempt = "EXEMPT";

    /// <summary>Legacy hidden plating default; R or empty keeps the item code as the item group (MM-BR-808).</summary>
    public const string DefaultPlating = "R";

    // MM-BR-825: the item code limit is MM-BR-801
    private static readonly (string Name, Func<ItemFields, string?> Value, int Max)[] TextLimits =
    [
        (nameof(ItemFields.ItemName), f => f.ItemName, 100),
        (nameof(ItemFields.ItemName2), f => f.ItemName2, 100),
        (nameof(ItemFields.ItemDesc), f => f.ItemDesc, 100),
        (nameof(ItemFields.Unit), f => f.Unit, 5),
        (nameof(ItemFields.ItemClass), f => f.ItemClass, 16),
        (nameof(ItemFields.UpcCode), f => f.UpcCode, 20),
        (nameof(ItemFields.Location), f => f.Location, 15),
        (nameof(ItemFields.Memo1), f => f.Memo1, 50),
        (nameof(ItemFields.Memo2), f => f.Memo2, 50),
        (nameof(ItemFields.Memo3), f => f.Memo3, 50),
        (nameof(ItemFields.Memo4), f => f.Memo4, 50),
        (nameof(ItemFields.Memo5), f => f.Memo5, 50),
    ];

    private Item(long id, string itemCd, ItemFields fields, string? plating, decimal mvgCost, decimal onhandQty, decimal stkVal)
    {
        Id = id;
        ItemCd = itemCd;
        Fields = fields;
        Plating = plating;
        MvgCost = mvgCost;
        OnhandQty = onhandQty;
        StkVal = stkVal;
    }

    public long Id { get; }

    /// <summary>Never changes after the first save (MM-BR-801).</summary>
    public string ItemCd { get; }

    /// <summary>Checked values: blank text is null, required values and defaults are filled.</summary>
    public ItemFields Fields { get; private set; }

    public string? Plating { get; }

    /// <summary>Item code, or the item code without its last letter when plating is not R (MM-BR-808).</summary>
    public string ItemGroupCd => string.IsNullOrEmpty(Plating) || Plating == DefaultPlating ? ItemCd : ItemCd[..^1];

    public decimal MvgCost { get; }

    public decimal OnhandQty { get; }

    public decimal StkVal { get; }

    /// <summary>
    /// A new item. Type, unit, use flag, purchase price, taxable and VAT kind get their defaults when missing (MM-BR-802, 803, 831).
    /// The moving average cost is the purchase price; stock quantity and value are 0 (MM-BR-804).
    /// Code uniqueness and code values are checked by the caller.
    /// </summary>
    /// <exception cref="DomainException">MM-E-801, 802, 803, 825, 831.</exception>
    public static Item Create(string? itemCd, ItemFields fields)
    {
        var code = itemCd?.Trim() ?? "";
        if (code.Length == 0 || code.Length > ItemCdMaxLength || code.Any(c => char.IsLower(c) || char.IsWhiteSpace(c)))
        {
            throw new DomainException(InvalidCodeCode, code);
        }

        var normalized = Normalize(fields);
        normalized = normalized with
        {
            ItemType = normalized.ItemType ?? DefaultItemType,
            Unit = normalized.Unit ?? DefaultUnit,
            ValidFlag = normalized.ValidFlag ?? ValidFlagYes,
            UsdPrc = normalized.UsdPrc ?? DefaultUsdPrc,
        };
        Check(normalized);
        return new Item(0, code, normalized, DefaultPlating, normalized.UsdPrc!.Value, 0, 0);
    }

    /// <summary>An item read from the database.</summary>
    public static Item Restore(long id, string itemCd, ItemFields fields, string? plating, decimal mvgCost, decimal onhandQty, decimal stkVal) =>
        new(id, itemCd, Normalize(fields), plating, mvgCost, onhandQty, stkVal);

    /// <summary>
    /// Replaces the editable values. The moving average cost stays even when the purchase price changes (MM-BR-805).
    /// </summary>
    /// <param name="typeLocked">The item has stock in any warehouse, stock movements, or set structure (MM-BR-806).</param>
    /// <exception cref="DomainException">MM-E-802, 803, 806, 825, 831.</exception>
    public void Change(ItemFields fields, bool typeLocked)
    {
        var normalized = Normalize(fields);
        Check(normalized);
        if (typeLocked && normalized.ItemType != Fields.ItemType)
        {
            throw new DomainException(TypeLockedCode, ItemCd);
        }
        Fields = normalized;
    }

    /// <summary>An item used by any document, price, set structure, alternate item, cart or stock movement cannot be deleted (MM-BR-807).</summary>
    /// <exception cref="DomainException">MM-E-807.</exception>
    public void EnsureDeletable(bool inUse)
    {
        if (inUse)
        {
            throw new DomainException(InUseCode, ItemCd);
        }
    }

    /// <summary>New document lines, prices and set structure take only items in use (MM-BR-809). Existing lines are not checked.</summary>
    /// <exception cref="DomainException">MM-E-809.</exception>
    public void EnsureSelectable()
    {
        if (Fields.ValidFlag == ValidFlagNo)
        {
            throw new DomainException(NotInUseCode, ItemCd);
        }
    }

    /// <summary>
    /// Unit price default of a document line (MM-BR-832): the standard price is in the base currency, so a foreign currency
    /// document gets standard price × exchange rate rounded to the unit price decimals (BC-BR-422).
    /// </summary>
    /// <param name="exchangeRate">Base currency per document currency unit; 1 for a base currency document.</param>
    public static decimal DocumentUnitPrice(decimal standardPrice, decimal exchangeRate, int unitPriceDecimals, RoundingMode mode) =>
        Rounding.Round(standardPrice * exchangeRate, unitPriceDecimals, mode);

    /// <summary>Trimmed values, blank text as null, and defaults for taxable, VAT kind, sales price and reorder point.</summary>
    public static ItemFields Normalize(ItemFields f) => f with
    {
        ItemName = Clean(f.ItemName),
        ItemName2 = Clean(f.ItemName2),
        ItemDesc = Clean(f.ItemDesc),
        ItemType = Clean(f.ItemType),
        Unit = Clean(f.Unit),
        ItemClass = Clean(f.ItemClass),
        ValidFlag = Clean(f.ValidFlag),
        UpcCode = Clean(f.UpcCode),
        Location = Clean(f.Location),
        Memo1 = Clean(f.Memo1),
        Memo2 = Clean(f.Memo2),
        Memo3 = Clean(f.Memo3),
        Memo4 = Clean(f.Memo4),
        Memo5 = Clean(f.Memo5),
        ItemGrade = Clean(f.ItemGrade),
        Taxable = f.Taxable ?? true,
        VatKind = Clean(f.VatKind) ?? VatTaxable,
        SoUsdPrc = f.SoUsdPrc ?? 0,
        ReorderPoint = f.ReorderPoint ?? 0,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Check(ItemFields f)
    {
        foreach (var (name, value) in new (string, object?)[]
        {
            (nameof(ItemFields.ItemName), f.ItemName), (nameof(ItemFields.ItemType), f.ItemType), (nameof(ItemFields.Unit), f.Unit),
            (nameof(ItemFields.ItemClass), f.ItemClass), (nameof(ItemFields.ValidFlag), f.ValidFlag), (nameof(ItemFields.UsdPrc), f.UsdPrc),
        })
        {
            if (value is null)
            {
                throw new DomainException(RequiredCode, name);
            }
        }
        foreach (var (name, value) in new (string, decimal?)[]
        {
            (nameof(ItemFields.UsdPrc), f.UsdPrc), (nameof(ItemFields.SoUsdPrc), f.SoUsdPrc), (nameof(ItemFields.ReorderPoint), f.ReorderPoint),
        })
        {
            if (value < 0)
            {
                throw new DomainException(NegativeCode, name);
            }
        }
        foreach (var (name, value, max) in TextLimits)
        {
            if (value(f)?.Length > max)
            {
                throw new DomainException(TooLongCode, name, max);
            }
        }
        if (f.VatKind is not (VatTaxable or VatExempt))
        {
            throw new DomainException(InvalidVatKindCode, f.VatKind ?? "");
        }
    }
}

using System.Text.RegularExpressions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Common.Codes;

/// <summary>Minor code fields after normalizing. Names are the sent changes by language code; null removes a name (BC-BR-405).</summary>
public sealed record CodeMinorFields(IReadOnlyDictionary<string, string?> Names, int SortOrder, bool UseYn, string? Attr1, string? Attr2);

/// <summary>A Minor code that a save would leave in its Major: code and sort order (SD-BR-911, SD-BR-913).</summary>
public sealed record CodeMinorNode(string MinorCode, int SortOrder);

/// <summary>Minor code rules (SD-BR-911~916). Every error names the Minor code of the failing row.</summary>
public static partial class CodeMinorRules
{
    public const string InvalidCodeCode = "SD-E-911";
    public const string CodeChangedCode = "SD-E-912";
    public const string InvalidSortOrderCode = "SD-E-913";
    public const string InUseCode = "SD-E-914";
    public const string ProtectedCode = "SD-E-916";

    public const int MaxCodeLength = 20;

    /// <summary>
    /// SD-BR-916: Minor codes that code compares as constants. They cannot be deleted or set not in use.
    /// Add a Major's constants here when Domain starts to use them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ProtectedMinors = new Dictionary<string, IReadOnlySet<string>>
    {
        [CodeMajors.ItemType] = Set(ItemStock.Inventory, ItemStock.Set, ItemStock.Service),
        [CodeMajors.MovementType] = Set(
            MovementType.GoodsReceipt, MovementType.Return, MovementType.SetAssembly, MovementType.UnplannedReceipt, MovementType.StockCount,
            MovementType.GoodsIssue, MovementType.Delivery, MovementType.StockTransfer),
        [CodeMajors.Plating] = Set(Item.DefaultPlating),
        [CodeMajors.Unit] = Set(Item.DefaultUnit),
        [CodeMajors.ValidFlag] = Set(Item.ValidFlagYes, Item.ValidFlagNo),
    };

    /// <summary>SD-BR-911: required; upper case letters, digits and _; at most 20 characters.</summary>
    public static string NormalizeCode(string? minorCode)
    {
        var code = minorCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || !CodePattern().IsMatch(code))
        {
            throw new DomainException(InvalidCodeCode, minorCode ?? "");
        }
        return code;
    }

    /// <summary>
    /// SD-BR-911, 913 for one row. A blank name removes that language's translation, but the en name is required.
    /// A missing in-use flag means in use.
    /// </summary>
    public static CodeMinorFields Normalize(
        string minorCode, IReadOnlyDictionary<string, string?>? names, int? sortOrder, bool? useYn, string? attr1, string? attr2)
    {
        var kept = CodeNames.Normalize(names, InvalidCodeCode, minorCode);
        if (sortOrder is not > 0)
        {
            throw new DomainException(InvalidSortOrderCode, minorCode);
        }
        return new CodeMinorFields(kept, sortOrder.Value, useYn ?? true, Blank(attr1), Blank(attr2));
    }

    /// <summary>SD-BR-912: a stored Minor code never changes. A blank code in a change keeps the stored one.</summary>
    public static void EnsureSameCode(string storedCode, string? requestedCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode) && !string.Equals(requestedCode.Trim(), storedCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(CodeChangedCode, storedCode);
        }
    }

    /// <summary>SD-BR-911, 913: codes and sort orders are unique among the Minor codes the save leaves in the Major.</summary>
    public static void EnsureUnique(IReadOnlyCollection<CodeMinorNode> minors)
    {
        if (minors.GroupBy(m => m.MinorCode, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } code)
        {
            throw new DomainException(InvalidCodeCode, code.Key);
        }
        if (minors.GroupBy(m => m.SortOrder).FirstOrDefault(g => g.Count() > 1) is { } order)
        {
            throw new DomainException(InvalidSortOrderCode, order.Last().MinorCode);
        }
    }

    /// <summary>SD-BR-916: a constant Minor code cannot be deleted or set not in use.</summary>
    public static void EnsureNotProtected(string majorCode, string minorCode)
    {
        if (IsProtected(majorCode, minorCode))
        {
            throw new DomainException(ProtectedCode, minorCode);
        }
    }

    public static bool IsProtected(string majorCode, string minorCode) =>
        ProtectedMinors.TryGetValue(majorCode, out var minors) && minors.Contains(minorCode);

    /// <summary>SD-BR-914: a Minor code that business data stores cannot be deleted; set it not in use instead.</summary>
    public static void EnsureDeletable(string majorCode, string minorCode, bool usedByData)
    {
        EnsureNotProtected(majorCode, minorCode);
        if (usedByData)
        {
            throw new DomainException(InUseCode, minorCode);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static HashSet<string> Set(params string[] codes) => new(codes, StringComparer.Ordinal);

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();
}

using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Sd.Assemblies;

/// <summary>One component part an assembly used: its line seq, Lot, qty and unit price (mm_assembly_line).</summary>
public sealed record AssemblyPart(int Seq, long ItemId, long LotId, decimal Qty, decimal Prc);

/// <summary>A part of a disassembly: Qty goes back to the Lot and at the price of the assembly line Seq.</summary>
public sealed record ReturnPart(int Seq, long ItemId, long LotId, decimal Qty, decimal Prc);

/// <summary>Set assembly and disassembly (SD-BR-514~516, SD-BR-518, MM-BR-716).</summary>
public static class AssemblyRules
{
    public const string Assemble = "ASSEMBLE";
    public const string Disassemble = "DISASSEMBLE";
    public const string Open = "OP";
    public const string Voided = "VO";

    /// <summary>
    /// A component's stock does not cover the assembly, whatever the negative stock mode. Args: Set item code, component item
    /// code, warehouse code, needed qty, available qty.
    /// </summary>
    public const string NotEnoughStockCode = "SD-E-514";

    /// <summary>
    /// A disassembly asks for more than the assembly has left, or names a voided assembly or a disassembly, or the assembly
    /// still has a disassembly and cannot be voided. Args: assembly number, qty left to disassemble.
    /// </summary>
    public const string DisassemblyCode = "SD-E-516";

    public static bool IsKind(string? kind) => kind is Assemble or Disassemble;

    /// <summary>
    /// SD-BR-514, SD-BR-518: the Locations and Lots of one component, in FIFO order (MM-BR-716), split when one is not enough.
    /// Plain components have only their ('*', '*') balance. The user does not choose them.
    /// </summary>
    /// <exception cref="DomainException">SD-E-514 when the balances add up to less than qty.</exception>
    public static IReadOnlyList<StockPick> Allocate(
        string setItemCd, string componentCd, string warehouseCode, IEnumerable<StockBalance> balances, decimal qty)
    {
        var picks = StockSuggestion.Fifo(balances, qty);
        var shortQty = StockSuggestion.ShortQty(picks, qty);
        if (shortQty > 0)
        {
            throw new DomainException(NotEnoughStockCode, setItemCd, componentCd, warehouseCode, qty, qty - shortQty);
        }
        return picks;
    }

    /// <summary>
    /// SD-BR-515: the Set's unit price is the components' issue amount ÷ the Set qty, rounded to the unit price decimals; the
    /// Set's amount stays the issue amount.
    /// </summary>
    public static decimal UnitPrice(decimal issueAmount, decimal setQty, int decimals, RoundingMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(setQty);
        return Rounding.Round(issueAmount / setQty, decimals, mode);
    }

    /// <summary>SD-BR-516: a disassembly takes at most what the assembly has not given back yet.</summary>
    /// <exception cref="DomainException">SD-E-516 when qty is above the qty left.</exception>
    public static void EnsureCanDisassemble(string assemblyNo, decimal qty, decimal qtyLeft)
    {
        if (qty > qtyLeft)
        {
            throw new DomainException(DisassemblyCode, assemblyNo, qtyLeft);
        }
    }

    /// <summary>
    /// SD-BR-516: the parts of one component a disassembly of setQty gives back. The component qty is setQty × the qty the
    /// assembly used per Set; it fills the assembly's lines in the reverse of their order (the last Lot used first), each up to
    /// what it used minus what earlier disassemblies gave back. Earlier disassemblies filled from the end too, so what they gave
    /// back is taken off the last lines first. Nothing is divided, so no decimals appear.
    /// </summary>
    /// <param name="used">The component's assembly lines in seq order.</param>
    /// <param name="assemblyQty">The assembly's Set qty.</param>
    /// <param name="returned">What open disassemblies of the assembly already gave back of this component.</param>
    public static IReadOnlyList<ReturnPart> Return(IReadOnlyList<AssemblyPart> used, decimal assemblyQty, decimal returned, decimal setQty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assemblyQty);
        var perSet = used.Sum(p => p.Qty) / assemblyQty;
        var left = setQty * perSet;
        var skip = returned;
        var parts = new List<ReturnPart>();
        foreach (var part in used.Reverse())
        {
            var open = part.Qty;
            var given = Math.Min(skip, open);
            skip -= given;
            open -= given;
            if (open == 0 || left == 0)
            {
                continue;
            }
            var take = Math.Min(open, left);
            parts.Add(new ReturnPart(part.Seq, part.ItemId, part.LotId, take, part.Prc));
            left -= take;
        }
        return parts;
    }
}

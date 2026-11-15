namespace QuroErp.Contracts.Sd.Assemblies;

/// <summary>Set assembly and disassembly API routes (SD-UC-055).</summary>
public static class AssemblyRoutes
{
    /// <summary>GET ?from=&amp;to=&amp;setItemId=&amp;kind= : assemblies and disassemblies, newest first, for any signed-in user.</summary>
    public const string Base = "api/sd/assemblies";

    /// <summary>GET one assembly or disassembly with its component lines.</summary>
    public const string ById = Base + "/{id:long}";

    /// <summary>GET ?setItemId=&amp;qty=&amp;warehouseId=&amp;assemblyDt= : the components and their FIFO allocation (SD-BR-518).</summary>
    public const string Preview = Base + "/preview";

    /// <summary>POST { qty, disassemblyDt, locationId }: disassembles a part of the assembly (SD-BR-516).</summary>
    public const string Disassemble = Base + "/{id:long}/disassemble";

    /// <summary>POST { updatedAt }: voids an assembly or a disassembly (MM-BR-601, SD-BR-516).</summary>
    public const string Void = Base + "/{id:long}/void";

    /// <summary>The most rows a list returns.</summary>
    public const int MaxListSize = 1000;
}

/// <summary>Assembly list conditions; every condition is optional.</summary>
public sealed class AssemblySearchQuery
{
    /// <summary>Assembly date from, inclusive.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Assembly date to, inclusive.</summary>
    public DateOnly? To { get; init; }

    public long? SetItemId { get; init; }

    /// <summary>ASSEMBLE or DISASSEMBLE; none lists both.</summary>
    public string? Kind { get; init; }
}

/// <summary>
/// One assembly (ASSEMBLE) or disassembly (DISASSEMBLE). Amt is the Set's amount: the components' issue amount of an assembly, or
/// what a disassembly gave back. QtyLeft is what an open assembly can still disassemble (0 for a disassembly or a voided one).
/// </summary>
public sealed record AssemblySummaryDto(
    long Id, string AssemblyNo, string Kind, DateOnly AssemblyDt, long SetItemId, string SetItemCd, string? SetItemName, decimal Qty,
    long WarehouseId, string WarehouseCode, long LocationId, string LocationCode, string? SetLotNo, long? SourceAssemblyId,
    string? SourceAssemblyNo, string Status, decimal Amt, decimal QtyLeft, DateTimeOffset UpdatedAt);

/// <summary>One component part: the Location and Lot it was taken from (assembly) or given back to (disassembly).</summary>
public sealed record AssemblyLineDto(
    int Seq, long ItemId, string ItemCd, string? ItemName, decimal Qty, long LocationId, string LocationCode, long LotId, string LotNo,
    decimal Prc, decimal Amt);

/// <summary>An assembly or disassembly with its component lines in seq order.</summary>
public sealed record AssemblyDto(AssemblySummaryDto Header, string? Remarks, IReadOnlyList<AssemblyLineDto> Lines);

/// <summary>Preview conditions (SD-BR-518). AssemblyDt null is today.</summary>
public sealed class AssemblyPreviewQuery
{
    public long? SetItemId { get; init; }

    public decimal? Qty { get; init; }

    public long? WarehouseId { get; init; }

    public DateOnly? AssemblyDt { get; init; }
}

/// <summary>One FIFO part of a component: take Qty from this Location and Lot at about Prc (the moving average cost now).</summary>
public sealed record AssemblyPreviewPickDto(long LocationId, string LocationCode, long LotId, string LotNo, decimal Qty, decimal Prc, decimal Amt);

/// <summary>A component's needed qty, its FIFO parts and what they do not cover (ShortQty above 0 stops the save, SD-E-514).</summary>
public sealed record AssemblyPreviewComponentDto(
    long ItemId, string ItemCd, string? ItemName, decimal ComponentQty, decimal NeededQty, decimal ShortQty,
    IReadOnlyList<AssemblyPreviewPickDto> Picks);

/// <summary>What an assembly would take; nothing is saved. Amt is the components' amount at their moving average cost now.</summary>
public sealed record AssemblyPreviewDto(long SetItemId, decimal Qty, long WarehouseId, IReadOnlyList<AssemblyPreviewComponentDto> Components, decimal Amt);

/// <summary>An assembly (SD-BR-514). LocationId is where the Set is received; null is Location '*'. AssemblyDt null is today.</summary>
public sealed record SaveAssemblyRequest(
    long? SetItemId, decimal? Qty, DateOnly? AssemblyDt, long? WarehouseId, long? LocationId, string? Remarks = null);

/// <summary>
/// A disassembly of a part of the assembly (SD-BR-516). LocationId is where Location-managed components are received; null and
/// other components use Location '*'. DisassemblyDt null is today.
/// </summary>
public sealed record DisassembleRequest(decimal? Qty, DateOnly? DisassemblyDt, long? LocationId);

/// <summary>A saved assembly or disassembly. SetLotNo is the Set's Lot, '*' when the Set has no Lot management.</summary>
public sealed record AssemblyCreatedDto(long Id, string AssemblyNo, string SetLotNo);

/// <summary>Voids the assembly or disassembly read at UpdatedAt.</summary>
public sealed record VoidAssemblyRequest(DateTimeOffset UpdatedAt);

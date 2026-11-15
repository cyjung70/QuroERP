using System.Data;
using QuroErp.Contracts.Sd.Assemblies;

namespace QuroErp.Application.Sd.Assemblies;

/// <summary>List conditions of mm_assembly, with at most Limit rows.</summary>
public sealed record AssemblyFilter(DateOnly? From, DateOnly? To, long? SetItemId, string? Kind, int Limit);

/// <summary>
/// One mm_assembly row with its codes. Amt is the sum of its lines; QtyLeft is what an open assembly can still disassemble (its
/// qty minus its open disassemblies), 0 otherwise.
/// </summary>
public sealed record AssemblyRow(
    long Id, string AssemblyNo, string Kind, DateOnly AssemblyDt, long SetItemId, string SetItemCd, string? SetItemName, decimal Qty,
    long WarehouseId, string WarehouseCode, long LocationId, string LocationCode, long? SetLotId, string? SetLotNo,
    long? SourceAssemblyId, string? SourceAssemblyNo, string Status, decimal Amt, decimal QtyLeft, string? Remarks, DateTimeOffset UpdatedAt)
{
    public AssemblySummaryDto ToSummary() =>
        new(Id, AssemblyNo, Kind, AssemblyDt, SetItemId, SetItemCd, SetItemName, Qty, WarehouseId, WarehouseCode, LocationId, LocationCode,
            SetLotNo, SourceAssemblyId, SourceAssemblyNo, Status, Amt, QtyLeft, UpdatedAt);
}

/// <summary>A new mm_assembly row; SetLotId is set after the Set movement (<see cref="IAssemblyRepository.SetSetLotAsync"/>).</summary>
public sealed record NewAssembly(
    string AssemblyNo, string Kind, DateOnly AssemblyDt, long SetItemId, decimal Qty, long WarehouseId, long LocationId,
    long? SetLotId, long? SourceAssemblyId, string? Remarks);

/// <summary>A new mm_assembly_line row: a component part taken (assembly) or given back (disassembly).</summary>
public sealed record NewAssemblyLine(int Seq, long ItemId, decimal Qty, long WarehouseId, long LocationId, long LotId, decimal Prc, decimal Amt);

/// <summary>A new mm_lot_link row: Qty of the component Lot went into the Set Lot (MM-BR-368).</summary>
public sealed record NewLotLink(long SetLotId, long ComponentLotId, decimal Qty);

/// <summary>An item as assembly needs it: its type, whether it is in use, and its moving average cost for the preview.</summary>
public sealed record AssemblyItem(long Id, string ItemCd, string? ItemName, string? Type, bool InUse, decimal MovingAverageCost);

/// <summary>mm_assembly, mm_assembly_line and mm_lot_link (SD-BR-514~516, MM-BR-368).</summary>
public interface IAssemblyRepository
{
    Task<IReadOnlyList<AssemblyRow>> SearchAsync(AssemblyFilter filter, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<AssemblyRow?> FindAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The row locked for a disassembly or a void, so two of them wait for each other.</summary>
    Task<AssemblyRow?> LockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The component lines in seq order.</summary>
    Task<IReadOnlyList<AssemblyLineDto>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssemblyItem>> FindItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The warehouse code, or null when there is no such warehouse.</summary>
    Task<string?> FindWarehouseCodeAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(NewAssembly assembly, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Set Lot of the row; a Lot the assembly made also remembers it (mm_lot.assembly_id, MM-BR-367).</summary>
    Task SetSetLotAsync(long id, long setLotId, bool madeByAssembly, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertLinesAsync(long id, IReadOnlyList<NewAssemblyLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertLotLinksAsync(long id, IReadOnlyList<NewLotLink> links, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>What the open disassemblies of the assembly gave back, per component item.</summary>
    Task<IReadOnlyDictionary<long, decimal>> FindReturnedQtyAsync(long assemblyId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The ids of the original movements of the document number that are not reversed yet.</summary>
    Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(string assemblyNo, IDbTransaction transaction, CancellationToken cancellationToken);

    Task VoidAsync(long id, string user, IDbTransaction transaction, CancellationToken cancellationToken);
}

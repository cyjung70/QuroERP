using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Warehouses;

/// <summary>Warehouse types (bc_warehouse.warehouse_type, MM-BR-709).</summary>
public static class WarehouseTypes
{
    public const string Normal = "NORMAL";

    /// <summary>The one warehouse for billed but unshipped stock. Only the system moves stock in and out of it.</summary>
    public const string Consignment = "CONSIGNMENT";
}

/// <summary>The consignment warehouse (MM-BR-709). Its code cannot change (MM-BR-701).</summary>
public sealed record ConsignmentWarehouse(long Id, string WarehouseCode);

/// <summary>A warehouse as it will be stored (MM-BR-701, MM-BR-702, MM-BR-709). Id is 0 for a new one.</summary>
public sealed record WarehouseState(long Id, string WarehouseCode, string WarehouseName, bool IsDefault, bool UseYn, string WarehouseType, string? Remarks);

/// <summary>Why a warehouse cannot be set not in use or deleted: the second arg of MM-E-704 (MM-BR-704).</summary>
public static class WarehouseBlockReasons
{
    /// <summary>An item has a balance other than 0 in the warehouse today.</summary>
    public const string OnHand = "ONHAND";

    /// <summary>A stock movement of the warehouse is dated after today.</summary>
    public const string FutureMovement = "FUTURE_MOVEMENT";

    /// <summary>The warehouse has stock movements, so it cannot be deleted.</summary>
    public const string HasMovement = "HAS_MOVEMENT";

    /// <summary>A document, stock row or count uses the warehouse, so it cannot be deleted.</summary>
    public const string InUse = "IN_USE";
}

/// <summary>Warehouse master rules (MM-UC-017, MM-BR-701~704, MM-BR-709~710).</summary>
public static partial class WarehouseRules
{
    /// <summary>Code or name not valid, a duplicate code, or a stored code changed. Arg: the code.</summary>
    public const string InvalidCode = "MM-E-701";

    /// <summary>Not exactly one default warehouse, or the default one set not in use or deleted. Arg: the code.</summary>
    public const string DefaultCode = "MM-E-702";

    /// <summary>A warehouse with stock cannot be set not in use or deleted. Args: the code, a <see cref="WarehouseBlockReasons"/> value.</summary>
    public const string InUseCode = "MM-E-704";

    /// <summary>The type changed, a consignment warehouse added, or the consignment warehouse made default, not in use or deleted. Arg: the code.</summary>
    public const string ConsignmentCode = "MM-E-709";

    /// <summary>A user picked the consignment warehouse for a document or as the default warehouse. Arg: warehouse code.</summary>
    public const string ConsignmentNotSelectableCode = "MM-E-710";

    public const int CodeMaxLength = 10;

    /// <summary>
    /// MM-BR-701: trimmed and in upper case; up to 10 characters of A-Z, 0-9, '-' and '_'.
    /// </summary>
    /// <exception cref="DomainException">MM-E-701 with the code as given.</exception>
    public static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        if (!CodePattern().IsMatch(normalized))
        {
            throw new DomainException(InvalidCode, code?.Trim() ?? "");
        }
        return normalized;
    }

    /// <summary>MM-BR-701: the name is required (not translated); empty remarks are stored as null.</summary>
    /// <exception cref="DomainException">MM-E-701 with the code.</exception>
    public static WarehouseState Normalize(WarehouseState warehouse)
    {
        var name = warehouse.WarehouseName.Trim();
        if (name.Length == 0)
        {
            throw new DomainException(InvalidCode, warehouse.WarehouseCode);
        }
        return warehouse with { WarehouseName = name, Remarks = string.IsNullOrWhiteSpace(warehouse.Remarks) ? null : warehouse.Remarks.Trim() };
    }

    /// <summary>
    /// MM-BR-701 and MM-BR-709 for a changed row: the code and the type stay as stored. An empty code or type in the request means
    /// unchanged.
    /// </summary>
    /// <exception cref="DomainException">MM-E-701 for another code, MM-E-709 for another type.</exception>
    public static void EnsureKeysUnchanged(WarehouseState stored, string? requestedCode, string? requestedType)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode) && !string.Equals(requestedCode.Trim(), stored.WarehouseCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(InvalidCode, requestedCode.Trim());
        }
        if (!string.IsNullOrWhiteSpace(requestedType) && !string.Equals(requestedType.Trim(), stored.WarehouseType, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ConsignmentCode, stored.WarehouseCode);
        }
    }

    /// <summary>MM-BR-709: users add normal warehouses only. An empty type is NORMAL.</summary>
    /// <exception cref="DomainException">MM-E-709 with the code.</exception>
    public static string NewWarehouseType(string code, string? requestedType)
    {
        if (!string.IsNullOrWhiteSpace(requestedType) && !string.Equals(requestedType.Trim(), WarehouseTypes.Normal, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ConsignmentCode, code);
        }
        return WarehouseTypes.Normal;
    }

    /// <summary>MM-BR-702, MM-BR-709: the default and the consignment warehouse are never deleted.</summary>
    /// <exception cref="DomainException">MM-E-702 for the default, MM-E-709 for the consignment warehouse.</exception>
    public static void EnsureDeletable(WarehouseState stored)
    {
        if (stored.WarehouseType == WarehouseTypes.Consignment)
        {
            throw new DomainException(ConsignmentCode, stored.WarehouseCode);
        }
        if (stored.IsDefault)
        {
            throw new DomainException(DefaultCode, stored.WarehouseCode);
        }
    }

    /// <summary>
    /// Checks every warehouse as the batch save leaves them (MM-BR-701, MM-BR-702, MM-BR-709): codes unique ignoring case,
    /// exactly one default warehouse and it is in use, and the consignment warehouse is in use and not the default.
    /// </summary>
    /// <exception cref="DomainException">MM-E-701, MM-E-702 or MM-E-709.</exception>
    public static void EnsureFinalState(IReadOnlyCollection<WarehouseState> warehouses)
    {
        if (warehouses.GroupBy(w => w.WarehouseCode, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new DomainException(InvalidCode, duplicate.Key);
        }
        if (warehouses.FirstOrDefault(w => w.WarehouseType == WarehouseTypes.Consignment && (w.IsDefault || !w.UseYn)) is { } consignment)
        {
            throw new DomainException(ConsignmentCode, consignment.WarehouseCode);
        }
        var defaults = warehouses.Where(w => w.IsDefault).ToList();
        if (defaults.Count != 1)
        {
            throw new DomainException(DefaultCode, string.Join(", ", defaults.Select(w => w.WarehouseCode)));
        }
        if (!defaults[0].UseYn)
        {
            throw new DomainException(DefaultCode, defaults[0].WarehouseCode);
        }
    }

    /// <summary>
    /// MM-BR-710: users cannot pick the consignment warehouse for a document header or line, or as their default warehouse.
    /// Only the system's consignment movements use it.
    /// </summary>
    /// <param name="consignment">The consignment warehouse, null when the database has none.</param>
    /// <exception cref="DomainException">MM-E-710.</exception>
    public static void EnsureUserSelectable(IEnumerable<long?> warehouseIds, ConsignmentWarehouse? consignment)
    {
        if (consignment is not null && warehouseIds.Contains(consignment.Id))
        {
            throw new DomainException(ConsignmentNotSelectableCode, consignment.WarehouseCode);
        }
    }

    [GeneratedRegex("^[A-Z0-9_-]{1,10}$")]
    private static partial Regex CodePattern();
}

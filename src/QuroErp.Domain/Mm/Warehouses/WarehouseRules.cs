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

/// <summary>Which warehouses a user may pick (MM-BR-710).</summary>
public static class WarehouseRules
{
    /// <summary>A user picked the consignment warehouse for a document or as the default warehouse. Arg: warehouse code.</summary>
    public const string ConsignmentNotSelectableCode = "MM-E-710";

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
}

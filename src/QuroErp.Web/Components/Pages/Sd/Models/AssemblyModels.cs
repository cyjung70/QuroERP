using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>
/// Assembly header on the Set assembly screen (SD-UC-055). <see cref="AssemblyDto"/> is immutable, so the frame binds to this. The
/// Set is picked by code and the warehouse by code; the page keeps their ids.
/// </summary>
public sealed class AssemblyHeader
{
    public long Id { get; set; }
    public string? AssemblyNo { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; }
    public DateOnly? AssemblyDt { get; set; }
    public long? SetItemId { get; set; }
    public string? SetItemCd { get; set; }
    public string? SetItemName { get; set; }
    public decimal? Qty { get; set; }
    public string? WarehouseCode { get; set; }
    public string? LocationCode { get; set; }
    public string? SetLotNo { get; set; }
    public string? SourceAssemblyNo { get; set; }
    public string? Remarks { get; set; }
    public decimal Amt { get; set; }
    public decimal QtyLeft { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>A saved assembly or disassembly does not change; it can only be voided (MM-BR-601).</summary>
    public bool IsSaved => Id != 0;

    /// <summary>A row of the find dialog.</summary>
    public static AssemblyHeader From(AssemblySummaryDto a) => new()
    {
        Id = a.Id,
        AssemblyNo = a.AssemblyNo,
        Kind = a.Kind,
        Status = a.Status,
        AssemblyDt = a.AssemblyDt,
        SetItemId = a.SetItemId,
        SetItemCd = a.SetItemCd,
        SetItemName = a.SetItemName,
        Qty = a.Qty,
        WarehouseCode = a.WarehouseCode,
        LocationCode = a.LocationCode,
        SetLotNo = a.SetLotNo,
        SourceAssemblyNo = a.SourceAssemblyNo,
        Amt = a.Amt,
        QtyLeft = a.QtyLeft,
        UpdatedAt = a.UpdatedAt,
    };

    public static AssemblyHeader From(AssemblyDto a)
    {
        var header = From(a.Header);
        header.Remarks = a.Remarks;
        return header;
    }
}

/// <summary>
/// A component row of the assembly grid (sd.assembly.entry), read only: a FIFO part of the preview (SD-BR-518) or a saved line.
/// ShortQty is what the stock does not cover in the preview; a short component without stock has a row with no Location or Lot.
/// </summary>
public sealed class AssemblyLine
{
    public int Seq { get; set; }
    public long ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public decimal? ComponentQty { get; set; }
    public decimal Qty { get; set; }
    public string? LocationCode { get; set; }
    public string? LotNo { get; set; }
    public decimal Prc { get; set; }
    public decimal Amt { get; set; }
    public decimal ShortQty { get; set; }

    public static AssemblyLine From(AssemblyLineDto l) => new()
    {
        Seq = l.Seq,
        ItemId = l.ItemId,
        ItemCd = l.ItemCd,
        ItemName = l.ItemName,
        Qty = l.Qty,
        LocationCode = l.LocationCode,
        LotNo = l.LotNo,
        Prc = l.Prc,
        Amt = l.Amt,
    };

    /// <summary>One row per FIFO part; a short component gets one more row with the short qty and no Location or Lot.</summary>
    public static IEnumerable<AssemblyLine> Rows(AssemblyPreviewDto preview)
    {
        var seq = 0;
        foreach (var c in preview.Components)
        {
            foreach (var p in c.Picks)
            {
                yield return new AssemblyLine
                {
                    Seq = ++seq,
                    ItemId = c.ItemId,
                    ItemCd = c.ItemCd,
                    ItemName = c.ItemName,
                    ComponentQty = c.ComponentQty,
                    Qty = p.Qty,
                    LocationCode = p.LocationCode,
                    LotNo = p.LotNo,
                    Prc = p.Prc,
                    Amt = p.Amt,
                };
            }
            if (c.ShortQty > 0)
            {
                yield return new AssemblyLine
                {
                    Seq = ++seq,
                    ItemId = c.ItemId,
                    ItemCd = c.ItemCd,
                    ItemName = c.ItemName,
                    ComponentQty = c.ComponentQty,
                    Qty = c.ShortQty,
                    ShortQty = c.ShortQty,
                };
            }
        }
    }
}

/// <summary>Assembly kinds and statuses (SD-BR-514, SD-BR-516) and their chips.</summary>
public static class AssemblyStatus
{
    public const string Assemble = "ASSEMBLE";
    public const string Disassemble = "DISASSEMBLE";
    public const string Open = "OP";
    public const string Void = "VO";

    public static IReadOnlyList<string> Kinds { get; } = [Assemble, Disassemble];

    public static ChipKind ChipOf(string? status) => status == Void ? ChipKind.Danger : ChipKind.Ok;

    /// <summary>A new assembly only: a saved one does not change.</summary>
    public static bool CanSave(AssemblyHeader a) => !a.IsSaved;

    /// <summary>A saved open row; an assembly with a disassembly is refused by the API (SD-E-516).</summary>
    public static bool CanVoid(AssemblyHeader a) => a.IsSaved && a.Status == Open;

    /// <summary>An open assembly with qty left to take apart (SD-BR-516).</summary>
    public static bool CanDisassemble(AssemblyHeader a) => CanVoid(a) && a.Kind == Assemble && a.QtyLeft > 0;
}

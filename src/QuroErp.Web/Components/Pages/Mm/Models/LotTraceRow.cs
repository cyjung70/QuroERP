using System.Globalization;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// A row of the Lot trace tree (MM-UC-040), read only. Key and ParentKey make the tree; DocTypeText is the row kind in the
/// user's language with the movement type, DocNoText the document number with its line, as the PDF and Excel show them
/// (MM-BR-369).
/// </summary>
public sealed class LotTraceRow
{
    /// <summary>Directions of the search.</summary>
    public const string Forward = LotTraceRoutes.Forward;
    public const string Backward = LotTraceRoutes.Backward;

    public int Key { get; set; }
    public int? ParentKey { get; set; }
    public int Level { get; set; }
    public string DocType { get; set; } = "";
    public string? DocTypeText { get; set; }
    public string? DocNoText { get; set; }
    public DateOnly? DocDt { get; set; }
    public string? WarehouseCode { get; set; }
    public string? PartyName { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? LotNo { get; set; }
    public decimal? Qty { get; set; }

    /// <param name="kindName">The row kind (LOT, GR, SHIP, INV, ASM, MOVE) in the user's language.</param>
    public static LotTraceRow From(LotTraceRowDto r, string kindName) => new()
    {
        Key = r.Key,
        ParentKey = r.ParentKey,
        Level = r.Level,
        DocType = r.DocType,
        DocTypeText = r.MovType is { Length: > 0 } type ? $"{kindName} ({type})" : kindName,
        DocNoText = r.DocNo is null ? null : r.DocSeq is { } seq ? $"{r.DocNo}-{seq.ToString(CultureInfo.InvariantCulture)}" : r.DocNo,
        DocDt = r.DocDt,
        WarehouseCode = r.WarehouseCode,
        PartyName = r.PartyName,
        ItemCd = r.ItemCd,
        ItemName = r.ItemName,
        LotNo = r.LotNo,
        Qty = r.Qty,
    };
}

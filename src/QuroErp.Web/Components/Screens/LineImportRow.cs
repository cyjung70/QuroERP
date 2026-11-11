namespace QuroErp.Web.Components.Screens;

/// <summary>Column titles of <see cref="QuroLineImportDialog{TSource}"/>; the screen gives them from its own resources.</summary>
public sealed record LineImportLabels(string DocumentNo, string Seq, string ItemCd, string ItemName, string Unit, string RemainingQty, string Qty, string Import);

/// <summary>A source document line in the import dialog: unchecked, with its remaining qty as the default qty to take.</summary>
public sealed class LineImportRow<TSource>(
    string documentNo, int seq, string? itemCd, string? itemName, string? unit, decimal remainingQty, TSource source)
{
    public string DocumentNo { get; } = documentNo;

    public int Seq { get; } = seq;

    public string? ItemCd { get; } = itemCd;

    public string? ItemName { get; } = itemName;

    public string? Unit { get; } = unit;

    public decimal RemainingQty { get; } = remainingQty;

    /// <summary>The line the screen makes its own line from.</summary>
    public TSource Source { get; } = source;

    public bool Selected { get; set; }

    public decimal? Qty { get; set; } = remainingQty;
}

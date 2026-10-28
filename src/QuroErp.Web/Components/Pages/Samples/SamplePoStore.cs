namespace QuroErp.Web.Components.Pages.Samples;

/// <summary>Header of the PO Entry sample (SM).</summary>
public sealed class SamplePo
{
    public long Id { get; set; }

    public string? PoNo { get; set; }

    public DateOnly? PoDt { get; set; }

    public string? VendorCode { get; set; }

    public string? VendorName { get; set; }

    public string? VendorMessage { get; set; }

    public SamplePo Copy() => (SamplePo)MemberwiseClone();
}

/// <summary>Line of the PO Entry sample.</summary>
public sealed class SamplePoLine
{
    public long Id { get; set; }

    public long PoId { get; set; }

    public string? ItemCode { get; set; }

    public string? ItemName { get; set; }

    public decimal? Qty { get; set; }

    public decimal? Fob { get; set; }

    /// <summary>
    /// Display value only: quantity × F.O.B, shown with the column format. The stored amount is calculated
    /// and rounded by the API (IAmountRounder); a screen never rounds amounts itself.
    /// </summary>
    public decimal Amount => (Qty ?? 0) * (Fob ?? 0);

    public SamplePoLine Copy() => (SamplePoLine)MemberwiseClone();
}

/// <summary>Row of the vendor and item lookup dialogs.</summary>
public sealed record SampleLookupRow(string Code, string Name, decimal? DefaultPrice = null);

/// <summary>
/// In-memory data of the PO Entry sample page. Development only; real screens call the API.
/// Callers get copies, like data that went through an API.
/// </summary>
public sealed class SamplePoStore
{
    private readonly Lock _lock = new();
    private readonly List<SampleLookupRow> _vendors =
    [
        new("V0001", "ABC Trading Co."),
        new("V0002", "Busan Electronics"),
        new("V0003", "Global Parts Inc."),
    ];
    private readonly List<SampleLookupRow> _items =
    [
        new("COMIX010", "Computer etc.", 850.00m),
        new("KBD10001", "Keyboard USB", 12.50m),
        new("MON24001", "Monitor 24 inch", 180.00m),
        new("MOU10001", "Mouse optical", 6.80m),
    ];
    private readonly List<SamplePo> _pos =
    [
        new() { Id = 1, PoNo = "PO261000001", PoDt = new DateOnly(2026, 10, 1), VendorCode = "V0001", VendorName = "ABC Trading Co." },
    ];
    private readonly List<SamplePoLine> _lines =
    [
        new() { Id = 1, PoId = 1, ItemCode = "COMIX010", ItemName = "Computer etc.", Qty = 2, Fob = 850.00m },
        new() { Id = 2, PoId = 1, ItemCode = "KBD10001", ItemName = "Keyboard USB", Qty = 10, Fob = 12.50m },
    ];
    private long _nextId = 100;
    private int _nextPoNo = 1;

    public IReadOnlyList<SampleLookupRow> FindVendors(string? keyword) => Find(_vendors, keyword);

    public IReadOnlyList<SampleLookupRow> FindItems(string? keyword) => Find(_items, keyword);

    public SampleLookupRow? Vendor(string? code) => _vendors.FirstOrDefault(v => v.Code == code);

    public SampleLookupRow? Item(string? code) => _items.FirstOrDefault(i => i.Code == code);

    public (SamplePo Po, IReadOnlyList<SamplePoLine> Lines)? FindPo(string? poNo)
    {
        lock (_lock)
        {
            var po = _pos.FirstOrDefault(p => p.PoNo == poNo);
            return po is null ? null : (po.Copy(), LinesOf(po.Id));
        }
    }

    /// <summary>Saves the header and the changed lines together; a new PO gets the next number.</summary>
    public (SamplePo Po, IReadOnlyList<SamplePoLine> Lines) SavePo(
        SamplePo po, IEnumerable<SamplePoLine> added, IEnumerable<SamplePoLine> updated, IEnumerable<SamplePoLine> deleted)
    {
        lock (_lock)
        {
            var saved = po.Copy();
            saved.VendorName = Vendor(saved.VendorCode)?.Name;
            if (saved.Id == 0)
            {
                saved.Id = ++_nextId;
                saved.PoNo = $"PO{saved.PoDt:yyMM}{++_nextPoNo:D5}";
                _pos.Add(saved);
            }
            else
            {
                _pos[_pos.FindIndex(p => p.Id == saved.Id)] = saved;
            }
            foreach (var line in added.Concat(updated))
            {
                var copy = line.Copy();
                copy.PoId = saved.Id;
                copy.ItemName = Item(copy.ItemCode)?.Name;
                if (copy.Id == 0)
                {
                    copy.Id = ++_nextId;
                    _lines.Add(copy);
                }
                else
                {
                    _lines[_lines.FindIndex(l => l.Id == copy.Id)] = copy;
                }
            }
            var deletedIds = deleted.Select(l => l.Id).ToHashSet();
            _lines.RemoveAll(l => deletedIds.Contains(l.Id));
            return (saved.Copy(), LinesOf(saved.Id));
        }
    }

    public void DeletePo(long id)
    {
        lock (_lock)
        {
            _pos.RemoveAll(p => p.Id == id);
            _lines.RemoveAll(l => l.PoId == id);
        }
    }

    private static List<SampleLookupRow> Find(List<SampleLookupRow> rows, string? keyword) =>
        [.. rows.Where(r => string.IsNullOrWhiteSpace(keyword)
                            || r.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))];

    private List<SamplePoLine> LinesOf(long poId) =>
        [.. _lines.Where(l => l.PoId == poId).OrderBy(l => l.Id).Select(l => l.Copy())];
}

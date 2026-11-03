namespace QuroErp.Web.Components.Pages.Samples;

/// <summary>Item of the S and M samples.</summary>
public sealed class SampleItem
{
    public long Id { get; set; }

    public string? Code { get; set; }

    public string? Name { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public decimal? Qty { get; set; }

    /// <summary>Supply type P (purchased) or M (made), shown as radio cards.</summary>
    public string? Supply { get; set; } = "P";

    public string? Remarks { get; set; }

    /// <summary>In use; the S sample's status chip and its deactivate command.</summary>
    public bool Active { get; set; } = true;

    public string? UpdatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public SampleItem Copy() => (SampleItem)MemberwiseClone();
}

/// <summary>Stock of an item in a warehouse, the S sample's read-only sub-grid.</summary>
public sealed record SampleStock(string Warehouse, decimal OnHand, decimal Allocated)
{
    public decimal Available => OnHand - Allocated;
}

/// <summary>Document that moved an item, the S sample's transactions tab.</summary>
public sealed record SampleTransaction(string DocNo, DateOnly DocDt, decimal Qty);

/// <summary>Header of the SM sample.</summary>
public sealed class SampleOrder
{
    public long Id { get; set; }

    public string? OrderNo { get; set; }

    public DateOnly? OrderDt { get; set; }

    public SampleOrder Copy() => (SampleOrder)MemberwiseClone();
}

/// <summary>Line of the SM sample and target row of the MM sample.</summary>
public sealed class SampleLine
{
    public long Id { get; set; }

    public long OwnerId { get; set; }

    public string? ItemCode { get; set; }

    public decimal? Qty { get; set; }

    public SampleLine Copy() => (SampleLine)MemberwiseClone();
}

/// <summary>Source row of the MM sample.</summary>
public sealed class SampleGroup
{
    public long Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>
/// In-memory data of the screen type sample pages (TASK-CORE-006). Development only; real screens call the API.
/// Callers get copies, like data that went through an API.
/// </summary>
public sealed class SampleStore
{
    private readonly Lock _lock = new();
    private readonly List<SampleItem> _items =
    [
        new() { Id = 1, Code = "A100", Name = "Apple", ValidFrom = new DateOnly(2026, 1, 1), Qty = 10 },
        new() { Id = 2, Code = "B200", Name = "Banana", ValidFrom = new DateOnly(2026, 2, 1), Qty = 20 },
    ];
    private readonly List<SampleOrder> _orders = [new() { Id = 1, OrderNo = "SO261000001", OrderDt = new DateOnly(2026, 10, 1) }];
    private readonly List<SampleLine> _lines =
    [
        new() { Id = 1, OwnerId = 1, ItemCode = "A100", Qty = 3 },
        new() { Id = 2, OwnerId = 1, ItemCode = "B200", Qty = 5 },
    ];
    private readonly List<SampleGroup> _groups = [new() { Id = 1, Code = "G1", Name = "Fruit" }, new() { Id = 2, Code = "G2", Name = "Snack" }];
    private readonly List<SampleLine> _members = [new() { Id = 1, OwnerId = 1, ItemCode = "A100", Qty = 1 }];
    private readonly Dictionary<string, SampleStock[]> _stock = new()
    {
        ["A100"] = [new("WH01", 10, 2), new("WH02", 5, 0)],
        ["B200"] = [new("WH01", 20, 5)],
    };
    private readonly Dictionary<string, SampleTransaction[]> _transactions = new()
    {
        ["A100"] = [new("PO261000001", new DateOnly(2026, 10, 1), 15), new("SO261000001", new DateOnly(2026, 10, 2), -3)],
    };
    private const string SampleUser = "sample";
    private long _nextId = 100;

    public SampleItem? FindItem(string? code)
    {
        lock (_lock)
        {
            return _items.FirstOrDefault(i => i.Code == code)?.Copy();
        }
    }

    /// <summary>How often <see cref="Stock"/> was read, so the sample tests can check that a tab reads once.</summary>
    public int StockReads { get; private set; }

    public IReadOnlyList<SampleStock> Stock(string? itemCode)
    {
        lock (_lock)
        {
            StockReads++;
            return itemCode is not null && _stock.TryGetValue(itemCode, out var stock) ? [.. stock] : [];
        }
    }

    public IReadOnlyList<SampleTransaction> Transactions(string? itemCode)
    {
        lock (_lock)
        {
            return itemCode is not null && _transactions.TryGetValue(itemCode, out var rows) ? [.. rows] : [];
        }
    }

    public IReadOnlyList<SampleItem> Items()
    {
        lock (_lock)
        {
            return [.. _items.OrderBy(i => i.Code).Select(i => i.Copy())];
        }
    }

    public SampleItem SaveItem(SampleItem item)
    {
        lock (_lock)
        {
            var saved = item.Copy();
            saved.UpdatedBy = SampleUser;
            saved.UpdatedAt = DateTimeOffset.Now;
            Upsert(_items, saved, i => i.Id, (i, id) => i.Id = id);
            return saved.Copy();
        }
    }

    /// <summary>Puts a stored item in or out of use and returns it as stored.</summary>
    public SampleItem SetItemActive(long id, bool active)
    {
        lock (_lock)
        {
            var item = _items.Single(i => i.Id == id);
            item.Active = active;
            item.UpdatedBy = SampleUser;
            item.UpdatedAt = DateTimeOffset.Now;
            return item.Copy();
        }
    }

    public void SaveItems(IEnumerable<SampleItem> added, IEnumerable<SampleItem> updated, IEnumerable<long> deleted)
    {
        lock (_lock)
        {
            foreach (var item in added.Concat(updated))
            {
                Upsert(_items, item.Copy(), i => i.Id, (i, id) => i.Id = id);
            }
            _items.RemoveAll(i => deleted.Contains(i.Id));
        }
    }

    public void DeleteItem(long id)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Id == id);
        }
    }

    /// <summary>Orders for the document search dialog.</summary>
    public IReadOnlyList<SampleOrder> FindOrders(string? keyword)
    {
        lock (_lock)
        {
            return [.. _orders.Where(o => string.IsNullOrWhiteSpace(keyword) || (o.OrderNo?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                              .OrderBy(o => o.OrderNo).Select(o => o.Copy())];
        }
    }

    public (SampleOrder Order, IReadOnlyList<SampleLine> Lines)? FindOrder(string? orderNo)
    {
        lock (_lock)
        {
            var order = _orders.FirstOrDefault(o => o.OrderNo == orderNo);
            return order is null ? null : (order.Copy(), LinesOf(_lines, order.Id));
        }
    }

    public (SampleOrder Order, IReadOnlyList<SampleLine> Lines) SaveOrder(
        SampleOrder order, IEnumerable<SampleLine> added, IEnumerable<SampleLine> updated, IEnumerable<SampleLine> deleted)
    {
        lock (_lock)
        {
            var saved = order.Copy();
            Upsert(_orders, saved, o => o.Id, (o, id) => o.Id = id);
            SaveLines(_lines, saved.Id, added, updated, deleted);
            return (saved.Copy(), LinesOf(_lines, saved.Id));
        }
    }

    public void DeleteOrder(long id)
    {
        lock (_lock)
        {
            _orders.RemoveAll(o => o.Id == id);
            _lines.RemoveAll(l => l.OwnerId == id);
        }
    }

    public IReadOnlyList<SampleGroup> Groups()
    {
        lock (_lock)
        {
            return [.. _groups];
        }
    }

    public IReadOnlyList<SampleLine> Members(long groupId)
    {
        lock (_lock)
        {
            return LinesOf(_members, groupId);
        }
    }

    public void SaveMembers(long groupId, IEnumerable<SampleLine> added, IEnumerable<SampleLine> updated, IEnumerable<SampleLine> deleted)
    {
        lock (_lock)
        {
            SaveLines(_members, groupId, added, updated, deleted);
        }
    }

    private void SaveLines(List<SampleLine> lines, long ownerId, IEnumerable<SampleLine> added, IEnumerable<SampleLine> updated, IEnumerable<SampleLine> deleted)
    {
        foreach (var line in added.Concat(updated))
        {
            var copy = line.Copy();
            copy.OwnerId = ownerId;
            Upsert(lines, copy, l => l.Id, (l, id) => l.Id = id);
        }
        var deletedIds = deleted.Select(l => l.Id).ToHashSet();
        lines.RemoveAll(l => deletedIds.Contains(l.Id));
    }

    private static List<SampleLine> LinesOf(List<SampleLine> lines, long ownerId) =>
        [.. lines.Where(l => l.OwnerId == ownerId).OrderBy(l => l.Id).Select(l => l.Copy())];

    private void Upsert<T>(List<T> list, T value, Func<T, long> id, Action<T, long> setId)
    {
        if (id(value) == 0)
        {
            setId(value, ++_nextId);
            list.Add(value);
            return;
        }
        var index = list.FindIndex(v => id(v) == id(value));
        if (index >= 0)
        {
            list[index] = value;
        }
    }
}

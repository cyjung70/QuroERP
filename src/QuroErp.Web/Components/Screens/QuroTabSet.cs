namespace QuroErp.Web.Components.Screens;

/// <summary>
/// Tabs registered by the <see cref="QuroTab"/> children of an S form, the open tab, and which record each tab
/// has read its data for (screen-templates.md 1).
/// </summary>
public sealed class QuroTabSet
{
    /// <summary>Id of the first tab, the saved fields.</summary>
    public const string FieldsId = "fields";

    private readonly List<QuroTab> _tabs = [];
    private readonly Dictionary<QuroTab, int> _loadedFor = [];

    public IReadOnlyList<QuroTab> Items => _tabs;

    public string Active { get; internal set; } = FieldsId;

    /// <summary>Changes whenever the form shows another record, so the tabs read their data again.</summary>
    internal int Record { get; private set; }

    internal void Add(QuroTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            _tabs.Add(tab);
        }
    }

    internal void NextRecord() => Record++;

    /// <summary>The open tab when it still has to read its data for the current record; it then counts as read.</summary>
    internal QuroTab? TakeActiveToLoad()
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == Active);
        if (tab?.LoadTab is null || (_loadedFor.TryGetValue(tab, out var record) && record == Record))
        {
            return null;
        }
        _loadedFor[tab] = Record;
        return tab;
    }
}

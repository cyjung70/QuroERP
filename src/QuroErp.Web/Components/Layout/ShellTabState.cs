namespace QuroErp.Web.Components.Layout;

/// <summary>
/// One screen tab as its screen sees it (app-shell.md 3.3), cascaded to the page drawn in the tab. The screen frame reports
/// whether the screen has unsaved changes and its title; the tab bar shows the dot and asks before closing. A screen outside
/// the tabs gets no state and keeps its own navigation guard.
/// </summary>
public sealed class ShellTabState(string key)
{
    /// <summary>The tab's key: the screen path of its menu, or the page route before its first parameter.</summary>
    public string Key { get; } = key;

    public bool IsDirty { get; private set; }

    /// <summary>The screen's title, the tab name of a screen that has no menu.</summary>
    public string? Title { get; private set; }

    /// <summary>Raised when the dirty state or the title changes.</summary>
    public event Action? Changed;

    public void Report(bool isDirty, string? title)
    {
        if (IsDirty == isDirty && Title == title)
        {
            return;
        }
        IsDirty = isDirty;
        Title = title;
        Changed?.Invoke();
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace QuroErp.Web.Components.Layout;

/// <summary>
/// The page of one screen tab with its tab state (app-shell.md 3.3 "전환"). It draws the page again only for a new document in the
/// tab (a new version); a redraw of the tab bar (a dirty dot, another tab) leaves the screen and its parameters alone.
/// </summary>
public sealed class ShellTabPane : ComponentBase
{
    private int? _drawn;

    [Parameter, EditorRequired]
    public Type Page { get; set; } = default!;

    [Parameter]
    public IReadOnlyDictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();

    [Parameter, EditorRequired]
    public ShellTabState State { get; set; } = default!;

    /// <summary>Raised by the tab for each new document; the page is created again.</summary>
    [Parameter]
    public int Version { get; set; }

    protected override bool ShouldRender() => _drawn != Version;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _drawn = Version;
        builder.OpenComponent<CascadingValue<ShellTabState>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<ShellTabState>.Value), State);
        builder.AddComponentParameter(2, nameof(CascadingValue<ShellTabState>.IsFixed), true);
        builder.AddComponentParameter(3, nameof(CascadingValue<ShellTabState>.ChildContent), (RenderFragment)(page =>
        {
            page.OpenComponent(0, Page);
            foreach (var (name, value) in Values)
            {
                page.AddComponentParameter(1, name, value);
            }
            page.SetKey(Version);
            page.CloseComponent();
        }));
        builder.CloseComponent();
    }
}

using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace QuroErp.Web.Components.Layout;

/// <summary>A path matched to its page: the component, its route values and the tab key (the template before its first parameter).</summary>
public sealed record ShellRoute(Type Page, IReadOnlyDictionary<string, object?> Values, string Key);

/// <summary>
/// Finds the page of a screen path for the screen tabs (app-shell.md 3.3), which draw pages themselves instead of the router.
/// Templates come from the pages' @page directives: literal segments and {Name} or {Name:constraint} parameters, the value
/// converted to the page parameter's type. Read once per app. Only interactive pages (with a render mode) are tab pages.
/// </summary>
public static class ShellRoutes
{
    private sealed record Template(Type Page, string[] Segments, string Key);

    private static readonly Lazy<IReadOnlyList<Template>> Templates = new(() =>
    [
        .. typeof(ShellRoutes).Assembly.GetTypes()
            // Interactive pages only: the static ones (home, sign-in, error) need the request and are not drawn in a tab
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttribute<RenderModeAttribute>(true) is not null)
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => Parse(t, r.Template)))
            // Literal routes before parameter routes, longer before shorter
            .OrderBy(t => t.Segments.Count(IsParameter))
            .ThenByDescending(t => t.Segments.Length),
    ]);

    /// <summary>The page of a path such as /samples/po-entry/SO1?x=1, or null when no page has that route.</summary>
    public static ShellRoute? Resolve(string path)
    {
        var segments = PathOnly(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var template in Templates.Value)
        {
            if (Match(template, segments) is { } values)
            {
                return new ShellRoute(template.Page, values, template.Key);
            }
        }
        return null;
    }

    /// <summary>The path without its query or fragment, without a trailing slash ("/" stays).</summary>
    public static string PathOnly(string path)
    {
        var only = path.Split('?', '#')[0];
        only = only.Length > 1 ? only.TrimEnd('/') : only;
        return only.Length == 0 ? "/" : only;
    }

    private static Template Parse(Type page, string template)
    {
        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var literal = segments.TakeWhile(s => !IsParameter(s)).ToArray();
        return new Template(page, segments, "/" + string.Join('/', literal));
    }

    private static bool IsParameter(string segment) => segment.StartsWith('{') && segment.EndsWith('}');

    private static Dictionary<string, object?>? Match(Template template, string[] segments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var required = template.Segments.Count(s => !IsParameter(s) || !s.Contains('?', StringComparison.Ordinal));
        if (segments.Length > template.Segments.Length || segments.Length < required)
        {
            return null;
        }
        for (var i = 0; i < template.Segments.Length; i++)
        {
            var part = template.Segments[i];
            if (!IsParameter(part))
            {
                if (i >= segments.Length || !string.Equals(part, segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                continue;
            }
            var name = part[1..^1].Split(':')[0].TrimEnd('?');
            if (i >= segments.Length)
            {
                continue;
            }
            var property = template.Page.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (Convert(Uri.UnescapeDataString(segments[i]), property?.PropertyType) is not { } value)
            {
                return null;
            }
            values[property?.Name ?? name] = value;
        }
        return values;
    }

    private static object? Convert(string text, Type? type)
    {
        var target = type is null ? typeof(string) : Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return text;
        }
        if (target == typeof(int))
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        }
        if (target == typeof(long))
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
        }
        if (target == typeof(Guid))
        {
            return Guid.TryParse(text, out var g) ? g : null;
        }
        return null;
    }
}

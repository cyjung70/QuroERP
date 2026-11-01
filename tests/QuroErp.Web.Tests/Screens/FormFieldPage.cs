using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// Page object of an input wrapper on a form (TASK-CORE-057). A field bound to the form's data set draws a Quro.UI control and
/// keeps its test id on the field element (TODO(QuroGrid-R3)); an unbound one keeps it on the input. The field is found again on
/// every call, so a re-render never leaves a stale element.
/// </summary>
public sealed class FormFieldPage(Func<IElement> find)
{
    private IElement Root => find();

    private bool IsCombo => Root.QuerySelector(".quro-ui-combo-choice") is not null;

    /// <summary>The element a user types in: the input itself, or the first visible input, text area or select of the field.</summary>
    public IElement Input => Root.LocalName is "input" or "textarea" or "select"
        ? Root
        : Root.QuerySelector("input:not([type=hidden]), textarea, select") ?? Root;

    /// <summary>The shown value: the text of an input, the code of the chosen combo or select option.</summary>
    public string? Value => IsCombo || Input.LocalName == "select" ? Selected : Input.GetAttribute("value");

    /// <summary>The code of the chosen option, or null. A combo code is in lower case (see <see cref="CodeOf"/>).</summary>
    public string? Selected => IsCombo
        ? Root.QuerySelector(".quro-ui-option[aria-selected=true]") is { } chosen ? CodeOf(chosen) : null
        : Input.QuerySelector("option[selected]")?.GetAttribute("value") ?? Input.GetAttribute("value");

    /// <summary>The options as code and shown text, without the empty entry.</summary>
    public IReadOnlyList<(string Code, string Text)> Options => IsCombo
        ? [.. Root.QuerySelectorAll(".quro-ui-option").Select(o => (CodeOf(o), o.GetAttribute("data-display") ?? ""))]
        : [.. Input.QuerySelectorAll("option").Where(o => !string.IsNullOrEmpty(o.GetAttribute("value")))
            .Select(o => (o.GetAttribute("value")!, o.TextContent.Trim()))];

    public bool IsReadOnly => IsCombo
        ? Root.QuerySelector("[data-quro-readonly=true]") is not null
        : Input.HasAttribute("readonly") || Input.HasAttribute("disabled");

    /// <summary>Types a value, or for a combo or select chooses the option with that code.</summary>
    public void Change(string? value)
    {
        if (!IsCombo)
        {
            Input.Change(value);
            return;
        }
        var options = Root.QuerySelectorAll(".quro-ui-option").ToList();
        var index = options.FindIndex(o => string.Equals(CodeOf(o), value, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException($"The combo has no option '{value}'.");
        }
        Root.QuerySelector(".quro-ui-combo-choice")!.Change(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // The combo puts only its search text "code text" in lower case on an option, so a combo code comes back in lower case
    // (TODO(QuroGrid-R3)); compare it without case
    private static string CodeOf(IElement option)
    {
        var search = option.GetAttribute("data-search") ?? "";
        var space = search.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? search : search[..space];
    }
}

public static class FormFieldPageExtensions
{
    /// <summary>The input wrapper with this test id.</summary>
    public static FormFieldPage Field<TPage>(this IRenderedComponent<TPage> page, string testId)
        where TPage : IComponent => new(() => page.Find($"[data-testid={testId}]"));
}

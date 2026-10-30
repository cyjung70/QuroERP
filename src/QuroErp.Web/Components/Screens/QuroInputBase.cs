using Microsoft.AspNetCore.Components;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// Base of the Quro inputs: label with a required mark, lock rules from <see cref="LockMode"/>,
/// registration for the form's required check and a <c>data-testid</c>.
/// </summary>
public abstract class QuroInputBase<TValue> : ComponentBase, IQuroField, IDisposable
{
    [CascadingParameter]
    public QuroFormContext? Form { get; set; }

    /// <summary>Translated label (a resource value).</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    [Parameter]
    public TValue? Value { get; set; }

    [Parameter]
    public EventCallback<TValue?> ValueChanged { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public LockMode LockMode { get; set; }

    /// <summary>Hides the label visually (grid cells). Screen readers still get it.</summary>
    [Parameter]
    public bool HideLabel { get; set; }

    /// <summary>Written as <c>data-testid</c> on the input element.</summary>
    [Parameter]
    public string? TestId { get; set; }

    /// <summary>Columns the field takes in a 4-column header (entry-frame.md 3): 1, 2 or 4.</summary>
    [Parameter]
    public int Span { get; set; } = 1;

    protected string InputId { get; } = "q" + Guid.NewGuid().ToString("N")[..10];

    public bool IsLocked => Form is { ReadOnly: true } || LockMode switch
    {
        LockMode.Always => true,
        LockMode.NewOnly or LockMode.AfterLoad => Form is { IsNew: false },
        _ => false,
    };

    public bool IsRequiredNow => !IsLocked && (Required || LockMode == LockMode.NewOnly);

    /// <summary>
    /// Field color by state (entry-frame.md 3): editable yellow, locked gray, required amber marker. Screens never set colors.
    /// </summary>
    protected string FieldClass(string? extra = null) => string.Join(' ', new[]
    {
        "quro-field",
        extra,
        IsLocked ? "quro-field-readonly" : "quro-field-editable",
        IsRequiredNow ? "quro-field-required" : null,
        Span is 2 or 4 ? $"quro-span-{Span}" : null,
    }.Where(c => c is not null));

    public abstract bool IsEmpty { get; }

    protected override void OnInitialized() => Form?.Register(this);

    protected Task SetValueAsync(TValue? value) => ValueChanged.InvokeAsync(value);

    public void Dispose()
    {
        Form?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

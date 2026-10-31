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

    /// <summary>
    /// Written as <c>data-testid</c> on the input element; on the field element when the input is a Quro.UI control
    /// bound to <see cref="Field"/>.
    /// </summary>
    [Parameter]
    public string? TestId { get; set; }

    /// <summary>
    /// Column of the form's data set (an S form record property) to bind to instead of <see cref="Value"/>
    /// (qurogrid-integration.md 10). Without a data set form the input stays bound to <see cref="Value"/>.
    /// </summary>
    [Parameter]
    public string? Field { get; set; }

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

    /// <summary>The input is bound to a column of the form's data set.</summary>
    protected bool Bound => Field is not null && Form?.Data is not null;

    /// <summary>The bound column's value, or <see cref="Value"/>.</summary>
    protected TValue? Current => Bound ? (TValue?)Form!.Data!.GetValue(Field!) : Value;

    string? IQuroField.Field => Bound ? Field : null;

    protected override void OnInitialized()
    {
        Form?.Register(this);
        if (Bound)
        {
            Form!.Changed += OnFormChanged;
        }
    }

    // NewOnly fields are required only while the record is new, so the column follows the lock state
    protected override void OnParametersSet()
    {
        if (Bound)
        {
            Form!.Data!.Require(Field!, IsRequiredNow);
        }
    }

    private void OnFormChanged()
    {
        Form!.Data!.Require(Field!, IsRequiredNow);
        _ = InvokeAsync(StateHasChanged);
    }

    protected Task SetValueAsync(TValue? value)
    {
        if (Bound)
        {
            Form!.Data!.SetValue(Field!, value);
            return Task.CompletedTask;
        }
        return ValueChanged.InvokeAsync(value);
    }

    public void Dispose()
    {
        if (Form is not null)
        {
            Form.Changed -= OnFormChanged;
        }
        Form?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

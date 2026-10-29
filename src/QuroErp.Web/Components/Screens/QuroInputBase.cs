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

    protected string InputId { get; } = "q" + Guid.NewGuid().ToString("N")[..10];

    public bool IsLocked => Form is { ReadOnly: true } || LockMode switch
    {
        LockMode.Always => true,
        LockMode.NewOnly or LockMode.AfterLoad => Form is { IsNew: false },
        _ => false,
    };

    public bool IsRequiredNow => !IsLocked && (Required || LockMode == LockMode.NewOnly);

    public abstract bool IsEmpty { get; }

    protected override void OnInitialized() => Form?.Register(this);

    protected Task SetValueAsync(TValue? value) => ValueChanged.InvokeAsync(value);

    public void Dispose()
    {
        Form?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

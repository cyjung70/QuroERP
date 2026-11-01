using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Localization;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// Base of the Quro inputs: label with a required mark, lock rules from <see cref="LockMode"/> and the field policy
/// (<see cref="Policy"/>), registration for the form's required check and a <c>data-testid</c>.
/// </summary>
public abstract class QuroInputBase<TValue> : ComponentBase, IQuroField, IDisposable
{
    [CascadingParameter]
    public QuroFormContext? Form { get; set; }

    /// <summary>The screen's field policies, cascaded by the screen type component.</summary>
    [CascadingParameter]
    public QuroFieldPolicy? Policies { get; set; }

    [Inject]
    private IStringLocalizer<SharedResource> Localizer { get; set; } = default!;

    /// <summary>Translated label (a resource value). Without it the label is the policy's label key (<see cref="Policy"/>).</summary>
    [Parameter]
    public string Label { get; set; } = "";

    /// <summary>
    /// The field key, which is the field's label resource key (field-policy.md "필드 키"). The policy hides the field, shows
    /// the required marker or locks it; with <see cref="Required"/> and <see cref="LockMode"/> the stricter one applies.
    /// </summary>
    [Parameter]
    public string? Policy { get; set; }

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

    /// <summary>The label shown and used in messages: <see cref="Label"/>, or the policy's label key translated.</summary>
    public string ShownLabel =>
        !string.IsNullOrEmpty(Label) || Policy is null ? Label : Localizer[Policies?.LabelKeyOf(Policy) ?? Policy];

    string IQuroField.Label => ShownLabel;

    /// <summary>The policy hides the field (it may still be shown by "show all fields").</summary>
    public bool IsPolicyHidden => Policy is not null && Policies?.IsHidden(Policy) == true;

    /// <summary>The field is drawn: not hidden by the policy, or shown by "show all fields".</summary>
    protected bool Drawn => Policy is null || Policies?.Hides(Policy) != true;

    public bool IsLocked => Form is { ReadOnly: true } || (Policy is not null && Policies?.Locks(Policy) == true) || LockMode switch
    {
        LockMode.Always => true,
        LockMode.NewOnly or LockMode.AfterLoad => Form is { IsNew: false },
        _ => false,
    };

    public bool IsRequiredNow => !IsLocked && !IsPolicyHidden
        && (Required || LockMode == LockMode.NewOnly || (Policy is not null && Policies?.Requires(Policy) == true));

    bool IQuroField.IsExcluded => IsPolicyHidden;

    /// <summary>
    /// Field color by state (entry-frame.md 3): editable yellow, locked gray, required amber marker. Screens never set colors.
    /// A hidden field shown by "show all fields" gets the policy marker.
    /// </summary>
    protected string FieldClass(string? extra = null) => string.Join(' ', new[]
    {
        "quro-field",
        extra,
        IsLocked ? "quro-field-readonly" : "quro-field-editable",
        IsRequiredNow ? "quro-field-required" : null,
        IsPolicyHidden ? "quro-field-policy-hidden" : null,
        Span is 2 or 4 ? $"quro-span-{Span}" : null,
    }.Where(c => c is not null));

    /// <summary>The field's label for its input; required fields get a "*" (screens.md "필수 입력").</summary>
    protected RenderFragment FieldLabel(bool required) => builder => BuildLabel(builder, required);

    private void BuildLabel(RenderTreeBuilder builder, bool required)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "for", InputId);
        builder.AddAttribute(2, "class", HideLabel ? "quro-visually-hidden" : null);
        builder.AddContent(3, ShownLabel);
        if (required)
        {
            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", "quro-required");
            builder.AddAttribute(6, "aria-hidden", "true");
            builder.AddContent(7, "*");
            builder.CloseElement();
        }
        if (IsPolicyHidden)
        {
            // Only "show all fields" draws a hidden field (BC-BR-435)
            builder.OpenElement(8, "span");
            builder.AddAttribute(9, "class", "quro-policy-hidden-mark");
            builder.AddContent(10, Localizer["Common.HiddenByPolicy"]);
            builder.CloseElement();
        }
        builder.CloseElement();
    }

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
        if (Policies is not null)
        {
            Policies.Changed += OnPolicyChanged;
        }
    }

    // NewOnly fields are required only while the record is new, so the column follows the lock state
    protected override void OnParametersSet() => SyncColumn();

    private void OnFormChanged()
    {
        SyncColumn();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPolicyChanged()
    {
        SyncColumn();
        _ = InvokeAsync(StateHasChanged);
    }

    // The bound column is required as the input shows it; a field the policy hides leaves Validate's required check
    private void SyncColumn()
    {
        if (Bound)
        {
            Form!.Data!.Require(Field!, IsRequiredNow, IsPolicyHidden);
        }
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
        if (Policies is not null)
        {
            Policies.Changed -= OnPolicyChanged;
        }
        // A field the form no longer shows (e.g. the password of a loaded user) no longer makes its column required
        if (Bound)
        {
            Form!.Data!.Require(Field!, false);
        }
        Form?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

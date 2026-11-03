namespace QuroErp.Web.Components.Screens;

/// <summary>
/// How a field locks (screens.md "잠김 필드", AS IS tag second digit).
/// </summary>
public enum LockMode
{
    None,

    /// <summary>Tag 3: required while new, locked once the record is loaded or saved (e.g. a key).</summary>
    NewOnly,

    /// <summary>Tag 4: always read-only.</summary>
    Always,

    /// <summary>Tag 5: editable while new, locked once the record is loaded or saved.</summary>
    AfterLoad,
}

/// <summary>Letter case applied to typed text (screens.md "대문자 변환", AS IS tag third digit).</summary>
public enum TextCase
{
    None,
    Upper,
    Lower,
}

/// <summary>A form input known to its form, for the client-side required check.</summary>
public interface IQuroField
{
    string Label { get; }

    /// <summary>The data set column the input is bound to, or null for an input bound with @bind-Value.</summary>
    string? Field => null;

    bool IsRequiredNow { get; }

    /// <summary>The field policy hides the input, so its column leaves the data set's required check (field-policy.md 3).</summary>
    bool IsExcluded => false;

    bool IsEmpty { get; }
}

/// <summary>
/// State shared by a form and its inputs: whether the record is new (lock rules) and which inputs exist (required check).
/// </summary>
public sealed class QuroFormContext
{
    private readonly List<IQuroField> _fields = [];

    /// <summary>True until the record is loaded or saved.</summary>
    public bool IsNew { get; internal set; } = true;

    /// <summary>The user may only read (no Full Access, BC-BR-214): every input is locked.</summary>
    public bool ReadOnly { get; internal set; }

    /// <summary>The record as a one-row data set (S forms); inputs with a <c>Field</c> bind to it. Null in other forms.</summary>
    public QuroFormData? Data { get; internal set; }

    /// <summary>
    /// Raised when the record, its values or the lock state change. Bound inputs have no callback parameter, so the form
    /// drawing again does not draw them; they draw on this instead.
    /// </summary>
    public event Action? Changed;

    internal void NotifyChanged() => Changed?.Invoke();

    internal void Register(IQuroField field) => _fields.Add(field);

    internal void Unregister(IQuroField field) => _fields.Remove(field);

    /// <summary>The first required input that is empty, or null.</summary>
    public IQuroField? FirstMissing() => _fields.FirstOrDefault(f => f.IsRequiredNow && f.IsEmpty);

    /// <summary>
    /// Sets the required columns of the bound inputs as they are now. NewOnly inputs stop being required once the record is
    /// loaded, which does not always set their parameters again.
    /// </summary>
    internal void SyncRequired()
    {
        foreach (var field in _fields)
        {
            if (Data is not null && field.Field is { } column)
            {
                Data.Require(column, field.IsRequiredNow, field.IsExcluded);
            }
        }
    }

    /// <summary>The input bound to a data set column, for the label in a validation message.</summary>
    public IQuroField? Find(string field) =>
        _fields.FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase));
}

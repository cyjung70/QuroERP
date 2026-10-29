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

    bool IsRequiredNow { get; }

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

    internal void Register(IQuroField field) => _fields.Add(field);

    internal void Unregister(IQuroField field) => _fields.Remove(field);

    /// <summary>The first required input that is empty, or null.</summary>
    public IQuroField? FirstMissing() => _fields.FirstOrDefault(f => f.IsRequiredNow && f.IsEmpty);
}

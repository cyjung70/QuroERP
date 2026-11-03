using Quro.Grid.Definition;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Web.Bc.Localization;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// A screen's field policies as its inputs and grid columns see them (field-policy.md 3). The screen type component loads them
/// once and cascades this object; inputs with a <c>Policy</c> key and grid columns follow it, so screens never branch on a state.
/// A field without a policy is OPTIONAL and keeps its specification's required and lock rules.
/// </summary>
public sealed class QuroFieldPolicy
{
    private Dictionary<string, FieldPolicyDto> _fields = new(StringComparer.Ordinal);

    /// <summary>Raised when the policies arrive or "show all fields" changes, so the inputs draw again.</summary>
    public event Action? Changed;

    /// <summary>
    /// Admin "show all fields" (BC-BR-435): hidden fields are shown read-only with a marker. It changes nothing that is saved.
    /// </summary>
    public bool ShowAllFields { get; private set; }

    public bool IsEmpty => _fields.Count == 0;

    /// <summary>Goes up on every change, so a grid applies the policies to its definition again only then.</summary>
    public int Version { get; private set; }

    public void Load(IEnumerable<FieldPolicyDto> fields)
    {
        _fields = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);
        Version++;
        Changed?.Invoke();
    }

    public void SetShowAllFields(bool show)
    {
        if (ShowAllFields != show)
        {
            ShowAllFields = show;
            Version++;
            Changed?.Invoke();
        }
    }

    public string StateOf(string fieldKey) => _fields.GetValueOrDefault(fieldKey)?.State ?? FieldPolicyStates.Optional;

    /// <summary>The label resource key: the policy's override, or the field key itself.</summary>
    public string LabelKeyOf(string fieldKey) => _fields.GetValueOrDefault(fieldKey)?.LabelKey ?? fieldKey;

    /// <summary>The policy hides the field, whether or not "show all fields" shows it.</summary>
    public bool IsHidden(string fieldKey) => StateOf(fieldKey) == FieldPolicyStates.Hidden;

    /// <summary>The field is not drawn.</summary>
    public bool Hides(string fieldKey) => IsHidden(fieldKey) && !ShowAllFields;

    /// <summary>The field is read-only: READONLY, or a hidden field that "show all fields" shows.</summary>
    public bool Locks(string fieldKey) => StateOf(fieldKey) == FieldPolicyStates.ReadOnly || (IsHidden(fieldKey) && ShowAllFields);

    public bool Requires(string fieldKey) => StateOf(fieldKey) == FieldPolicyStates.Required;

    /// <summary>
    /// The grid with the policies of its columns, keyed by the column label (the field key): HIDDEN columns are hidden (or
    /// read-only while "show all fields" is on), READONLY columns read-only, REQUIRED columns required.
    /// </summary>
    public GridDef Apply(GridDef grid) => IsEmpty ? grid : grid with
    {
        Columns = [.. grid.Columns.Select(column => StateOf(column.Label) switch
        {
            FieldPolicyStates.Hidden when ShowAllFields => column with { Edit = ColumnEdit.ReadOnly, Req = false },
            FieldPolicyStates.Hidden => column with { Hidden = true, Req = false },
            FieldPolicyStates.ReadOnly => column with { Edit = ColumnEdit.ReadOnly, Req = false },
            FieldPolicyStates.Required when column.Edit != ColumnEdit.ReadOnly => column with { Req = true },
            _ => column,
        })],
    };
}

/// <summary>Field policies by screen, read once per circuit (field-policy.md 3). A failed read is tried again next time.</summary>
public sealed class FieldPolicyCache(FieldPolicyApiClient api)
{
    private readonly Dictionary<string, Task<IReadOnlyList<FieldPolicyDto>>> _screens = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<FieldPolicyDto>> GetAsync(string screenCode, CancellationToken cancellationToken)
    {
        if (!_screens.TryGetValue(screenCode, out var read))
        {
            read = api.GetAsync(screenCode, cancellationToken);
            _screens[screenCode] = read;
        }
        try
        {
            return await read;
        }
        catch
        {
            _screens.Remove(screenCode);
            throw;
        }
    }
}

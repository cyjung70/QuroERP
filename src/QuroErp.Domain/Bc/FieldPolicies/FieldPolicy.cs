namespace QuroErp.Domain.Bc.FieldPolicies;

/// <summary>The layer a merged value came from: the last layer that set any part of it.</summary>
public enum FieldPolicySource
{
    System,
    CountryPack,
    Company,
}

/// <summary>A field's merged policy.</summary>
/// <param name="LabelKey">The label override key, or the field key when no layer overrides it.</param>
public sealed record ResolvedField(string FieldKey, string State, string? DefaultValue, string LabelKey, FieldPolicySource Source);

/// <summary>
/// A screen's field policies, merged from the system default (OPTIONAL), the company country's pack and the company overrides;
/// a later layer covers an earlier one part by part (BC-BR-430, BC-BR-431).
/// </summary>
public sealed class FieldPolicy
{
    private readonly Dictionary<string, ResolvedField> _fields;

    private FieldPolicy(string screenCode, Dictionary<string, ResolvedField> fields)
    {
        ScreenCode = screenCode;
        _fields = fields;
    }

    public string ScreenCode { get; }

    /// <summary>Catalog fields in catalog order, then the declared ext. keys and other pack or company keys in key order.</summary>
    public IReadOnlyList<ResolvedField> Fields => [.. _fields.Values];

    /// <summary>The declared ext. keys of the screen (BC-BR-436).</summary>
    public IReadOnlyList<ResolvedField> ExtFields => [.. _fields.Values.Where(f => FieldCatalog.IsExtKey(f.FieldKey))];

    /// <summary>The merged policy; a field no layer names is OPTIONAL with its own key as the label.</summary>
    public ResolvedField Get(string fieldKey) =>
        _fields.GetValueOrDefault(fieldKey) ?? new ResolvedField(fieldKey, FieldState.Optional, null, fieldKey, FieldPolicySource.System);

    public bool IsDeclared(string fieldKey) => _fields.ContainsKey(fieldKey);

    /// <param name="pack">The company country's pack, or null when there is no company or no pack for its country.</param>
    /// <param name="company">The company overrides (bc_field_policy) by field key.</param>
    public static FieldPolicy Merge(string screenCode, CountryPack? pack, IReadOnlyDictionary<string, FieldPolicyEntry> company)
    {
        var prefix = screenCode + ".";
        var packEntries = pack?.Entries() ?? new Dictionary<string, FieldPolicyEntry>();
        var keys = (FieldCatalog.FieldsOf(screenCode) ?? []).Select(f => f.Key)
            .Concat(packEntries.Keys.Concat(company.Keys).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);

        var fields = new Dictionary<string, ResolvedField>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var field = new ResolvedField(key, FieldState.Optional, null, key, FieldPolicySource.System);
            field = Cover(field, packEntries.GetValueOrDefault(key), FieldPolicySource.CountryPack);
            field = Cover(field, company.GetValueOrDefault(key), FieldPolicySource.Company);
            fields[key] = field;
        }
        return new FieldPolicy(screenCode, fields);
    }

    private static ResolvedField Cover(ResolvedField field, FieldPolicyEntry? entry, FieldPolicySource source) =>
        entry is null || (entry.State, entry.DefaultValue, entry.LabelKey) is (null, null, null)
            ? field
            : new ResolvedField(
                field.FieldKey, entry.State ?? field.State, entry.DefaultValue ?? field.DefaultValue, entry.LabelKey ?? field.LabelKey, source);
}

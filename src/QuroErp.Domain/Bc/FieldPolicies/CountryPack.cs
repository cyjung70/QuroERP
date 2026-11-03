namespace QuroErp.Domain.Bc.FieldPolicies;

/// <summary>One layer's policy for a field. A null part keeps the value of the layer below (BC-BR-431).</summary>
/// <param name="State">One of <see cref="FieldState.All"/>, or null.</param>
/// <param name="DefaultValue">Value for new records, as text.</param>
/// <param name="LabelKey">Label override resource key; null keeps the field key as the label.</param>
public sealed record FieldPolicyEntry(string? State, string? DefaultValue, string? LabelKey);

/// <summary>
/// A country pack (countrypacks/{country}/, field-policy.md "국가팩"). Data only; country logic is in ICountryProfile.
/// </summary>
/// <param name="Policies">field-policy.yaml: field key → state, default and label override. ext. keys are declared here only.</param>
/// <param name="Defaults">defaults.yaml: field key → value for new records. It wins over a default in field-policy.yaml.</param>
/// <param name="Labels">Language code → label override key → text (labels.{lang}.yaml).</param>
public sealed record CountryPack(
    string CountryCode,
    IReadOnlyDictionary<string, FieldPolicyEntry> Policies,
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Labels)
{
    /// <summary>The pack layer: every key of field-policy.yaml and defaults.yaml with the default folded in.</summary>
    public IReadOnlyDictionary<string, FieldPolicyEntry> Entries()
    {
        var entries = new Dictionary<string, FieldPolicyEntry>(Policies, StringComparer.Ordinal);
        foreach (var (key, value) in Defaults)
        {
            entries[key] = (entries.GetValueOrDefault(key) ?? new FieldPolicyEntry(null, null, null)) with { DefaultValue = value };
        }
        return entries;
    }
}

using System.Collections;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.FieldPolicies;

/// <summary>Field policy rules (BC-BR-432, BC-BR-433, BC-BR-436, BC-BR-437).</summary>
public static class FieldPolicyRules
{
    /// <summary>A REQUIRED field is empty. Arg: the field's label key.</summary>
    public const string RequiredEmptyCode = "BC-E-432";

    /// <summary>A company override is not allowed: unknown key, invalid state or default, or HIDDEN on a required business field. Arg: field key.</summary>
    public const string InvalidPolicyCode = "BC-E-433";

    /// <summary>An ext_attrs key the company country's pack does not declare. Arg: field key.</summary>
    public const string UndeclaredExtCode = "BC-E-436";

    /// <summary>
    /// Checks one company override row and returns its trimmed key and entry. Blank parts are null, which keeps the pack value.
    /// The key must be a catalog field or an ext. key the pack declares.
    /// </summary>
    /// <exception cref="DomainException">BC-E-433 with the field key.</exception>
    public static (string FieldKey, FieldPolicyEntry Entry) NormalizeOverride(
        string? fieldKey, string? state, string? defaultValue, string? labelKey, CountryPack? pack)
    {
        var key = fieldKey?.Trim() ?? "";
        var entry = new FieldPolicyEntry(Blank(state)?.ToUpperInvariant(), Blank(defaultValue), Blank(labelKey));
        var known = FieldCatalog.Find(key) is not null || (FieldCatalog.IsExtKey(key) && pack?.Policies.ContainsKey(key) == true);
        if (!known || Problem(key, entry) is not null)
        {
            throw new DomainException(InvalidPolicyCode, key);
        }
        return (key, entry);
    }

    /// <summary>Why a layer's entry is not allowed, or null (BC-BR-433, BC-BR-437).</summary>
    public static string? Problem(string fieldKey, FieldPolicyEntry entry)
    {
        if (entry.State is not null && !FieldState.IsValid(entry.State))
        {
            return $"{fieldKey}: state {entry.State} is not one of {string.Join(", ", FieldState.All)}";
        }
        var field = FieldCatalog.Find(fieldKey);
        if (entry.State == FieldState.Hidden && field?.IsRequiredBusinessField == true)
        {
            return $"{fieldKey}: a required business field cannot be {FieldState.Hidden}";
        }
        if (entry.DefaultValue is { } value && field?.Kind == FieldValueKind.Country && !CountryProfiles.Supported.Contains(value))
        {
            return $"{fieldKey}: default {value} is not a supported country";
        }
        return null;
    }

    /// <summary>
    /// Country pack lint without the resources (BC-BR-437): every key is a catalog field or a declared ext. key, the states and
    /// defaults are valid, no required business field is HIDDEN, defaults name declared keys only, and every label override has a
    /// text in each language file. Returns one line per problem.
    /// </summary>
    public static IReadOnlyList<string> CheckPack(CountryPack pack)
    {
        var problems = new List<string>();
        foreach (var key in pack.Policies.Keys.Where(k => FieldCatalog.Find(k) is null && !FieldCatalog.IsExtKey(k)))
        {
            problems.Add($"{pack.CountryCode} field-policy.yaml {key}: not a field of a policy screen");
        }
        foreach (var key in pack.Defaults.Keys.Where(k => FieldCatalog.Find(k) is null && !pack.Policies.ContainsKey(k)))
        {
            problems.Add($"{pack.CountryCode} defaults.yaml {key}: not a field of a policy screen or a declared ext. key");
        }
        foreach (var (key, entry) in pack.Entries())
        {
            if (Problem(key, entry) is { } problem)
            {
                problems.Add($"{pack.CountryCode} {problem}");
            }
        }
        foreach (var labelKey in pack.Policies.Values.Select(e => e.LabelKey).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            foreach (var (lang, labels) in pack.Labels.Where(l => !l.Value.ContainsKey(labelKey)))
            {
                problems.Add($"{pack.CountryCode} labels.{lang}.yaml: no text for {labelKey}");
            }
        }
        return problems;
    }

    /// <summary>Null, blank text, or an empty collection (BC-BR-432).</summary>
    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        ICollection collection => collection.Count == 0,
        _ => false,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using System.Text.Json.Serialization;

namespace QuroErp.Contracts.Bc.FieldPolicies;

/// <summary>Field policy API routes (field-policy.md "적용 순서와 구현").</summary>
public static class FieldPolicyRoutes
{
    /// <summary>GET ?screen= (merged policies of a screen); PUT the company overrides.</summary>
    public const string Base = "api/bc/field-policies";

    public static string ForScreen(string screenCode) => $"{Base}?screen={Uri.EscapeDataString(screenCode)}";
}

/// <summary>Field policy states as the API sends them (field-policy.md "상태").</summary>
public static class FieldPolicyStates
{
    public const string Hidden = "HIDDEN";
    public const string Optional = "OPTIONAL";
    public const string Required = "REQUIRED";
    public const string ReadOnly = "READONLY";
}

/// <summary>A field's merged policy: system default, then the company country's pack, then the company override (BC-BR-431).</summary>
/// <param name="FieldKey">The label resource key, {Module}.{Screen}.{Property}; country-only fields are {Module}.{Screen}.ext.{Name}.</param>
/// <param name="State">HIDDEN, OPTIONAL, REQUIRED or READONLY.</param>
/// <param name="DefaultValue">Value for new records, as text, or null.</param>
/// <param name="LabelKey">The label resource key to show: an override key, or the field key.</param>
/// <param name="Source">The last layer that set a part of it: System, CountryPack or Company.</param>
public sealed record FieldPolicyDto(string FieldKey, string State, string? DefaultValue, string LabelKey, string Source);

/// <summary>A company override (bc_field_policy). A null part keeps the country pack value.</summary>
public sealed record FieldPolicyOverrideDto(
    long Id, string FieldKey, string? State, string? DefaultValue, string? LabelKey, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <summary>
/// PUT body: company overrides to set, all or nothing (BC-BR-433). Keys not sent keep their override. A row whose state, default
/// and label are all blank removes the override.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFieldPoliciesRequest(IReadOnlyList<FieldPolicyOverrideRow>? Rows);

/// <param name="State">HIDDEN, OPTIONAL, REQUIRED, READONLY, or blank to keep the pack state.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldPolicyOverrideRow(string? FieldKey, string? State, string? DefaultValue, string? LabelKey);

namespace QuroErp.Domain.Bc.FieldPolicies;

/// <summary>Field policy states (field-policy.md "상태", BC-BR-433).</summary>
public static class FieldState
{
    /// <summary>Not drawn. Input is ignored; new records get the default, edits keep the stored value; responses are null.</summary>
    public const string Hidden = "HIDDEN";

    /// <summary>Shown and editable, no check. The system default.</summary>
    public const string Optional = "OPTIONAL";

    /// <summary>Shown with the required marker; an empty value is 422 BC-E-432.</summary>
    public const string Required = "REQUIRED";

    /// <summary>Shown read-only. Input is ignored.</summary>
    public const string ReadOnly = "READONLY";

    public static IReadOnlyList<string> All { get; } = [Hidden, Optional, Required, ReadOnly];

    public static bool IsValid(string? state) => state is not null && All.Contains(state, StringComparer.Ordinal);

    /// <summary>Input for the field is ignored on save (BC-BR-434).</summary>
    public static bool IgnoresInput(string state) => state is Hidden or ReadOnly;
}

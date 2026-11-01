namespace QuroErp.Web.Components.Screens;

/// <summary>
/// A request to a lookup of a grid code column (<c>Definitions/lookups/**/{id}.lookup.toon</c>). The screen answers it from a
/// feature's Contracts API client (qurogrid-integration.md 4 "코드도움"); there is no generic query endpoint.
/// </summary>
/// <param name="LookupId">The lookup definition id.</param>
/// <param name="Keyword">Typed text for the popup cell and its dialog (null = all), or null when <paramref name="Codes"/> is set.</param>
/// <param name="Codes">Exact codes to read in one call, e.g. the pasted cells of a column (screens.md "그리드 붙여 넣기").</param>
/// <param name="Query">The named query of the lookup a rule calls, e.g. <c>byCustomer</c>; null for the first query.</param>
/// <param name="Args">Values of the query arguments, in the order of the lookup's <c>queries.params</c>.</param>
public sealed record LookupQuery(
    string LookupId,
    string? Keyword,
    IReadOnlyList<string>? Codes = null,
    string? Query = null,
    IReadOnlyList<object?>? Args = null);

/// <summary>One lookup result: values by the lookup's column ids (case is ignored).</summary>
public sealed class LookupRow
{
    private readonly Dictionary<string, object?> _values;

    public LookupRow(IEnumerable<KeyValuePair<string, object?>> values) =>
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);

    public object? this[string column] => _values.GetValueOrDefault(column);

    public bool Has(string column) => _values.ContainsKey(column);

    public static LookupRow Of(params (string Column, object? Value)[] values) =>
        new(values.Select(v => KeyValuePair.Create(v.Column, v.Value)));
}

namespace QuroErp.Application.Common;

/// <summary>
/// The change conflicts with existing data, e.g. reducing decimals that documents use (BC-BR-425).
/// The API returns 409 with ProblemDetails code and args.
/// </summary>
public sealed class ConflictException(string code, params object[] args) : Exception($"{code}: {string.Join(", ", args)}")
{
    public string Code { get; } = code;

    public IReadOnlyList<object> Args { get; } = args;
}

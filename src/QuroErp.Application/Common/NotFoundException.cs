namespace QuroErp.Application.Common;

/// <summary>The requested resource does not exist. The API returns 404 with ProblemDetails code and args.</summary>
public sealed class NotFoundException(string code, params object[] args) : Exception($"{code}: {string.Join(", ", args)}")
{
    public string Code { get; } = code;

    public IReadOnlyList<object> Args { get; } = args;
}

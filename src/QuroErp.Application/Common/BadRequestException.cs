namespace QuroErp.Application.Common;

/// <summary>The request cannot be processed as sent (for example a role that is not in use). The API returns 400 with code and args.</summary>
public sealed class BadRequestException(string code, params object[] args) : Exception($"{code}: {string.Join(", ", args)}")
{
    public string Code { get; } = code;

    public IReadOnlyList<object> Args { get; } = args;
}

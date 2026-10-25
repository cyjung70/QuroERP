namespace QuroErp.Domain.Common;

/// <summary>
/// Business rule violation. The API returns 422 with ProblemDetails code and args (api.md "오류");
/// Web shows the message for Code in the user's language.
/// </summary>
public class DomainException(string code, params object[] args) : Exception($"{code}: {string.Join(", ", args)}")
{
    /// <summary>Message code, e.g. SD-E-001.</summary>
    public string Code { get; } = code;

    /// <summary>Message arguments ({0}, {1} …).</summary>
    public IReadOnlyList<object> Args { get; } = args;
}

using QuroErp.Domain.Common;

namespace QuroErp.Application.Common;

/// <summary>
/// A warning the user must answer before the save goes on (api.md "경고"). Nothing is saved. The API returns 422 with
/// ProblemDetails code, args and confirmField: the request field to send again with the user's answer.
/// </summary>
public sealed class ConfirmationRequiredException(string code, string confirmField, params object[] args) : DomainException(code, args)
{
    /// <summary>Request body field (camelCase) that carries the answer, e.g. updateVendorPrice.</summary>
    public string ConfirmField { get; } = confirmField;
}

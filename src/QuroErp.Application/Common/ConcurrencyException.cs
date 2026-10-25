namespace QuroErp.Application.Common;

/// <summary>
/// Someone else changed the row after the client read it (updatedAt no longer matches).
/// The API returns 409 with ProblemDetails code Common-E-004 (api.md "오류").
/// </summary>
public sealed class ConcurrencyException() : Exception(Code)
{
    /// <summary>The data was changed by another user. Reload and try again.</summary>
    public const string Code = "Common-E-004";
}

using Microsoft.AspNetCore.Http;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Common.Errors;

/// <summary>
/// Gives every 403 Problem Details without a code the code Common-E-007 (api.md "오류", BC-BR-214): screen path policy denials,
/// admin-only policies and Forbid(). 403s that already carry a code (AUTH-004, AUTH-005) keep it.
/// </summary>
public static class ForbiddenProblem
{
    public static void AddCode(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if ((problem.Status ?? context.HttpContext.Response.StatusCode) == StatusCodes.Status403Forbidden
            && !problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = ProblemCodes.Forbidden;
            problem.Extensions["args"] = Array.Empty<string>();
        }
    }
}

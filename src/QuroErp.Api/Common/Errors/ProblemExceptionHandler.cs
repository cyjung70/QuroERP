using Microsoft.AspNetCore.Diagnostics;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common;

namespace QuroErp.Api.Common.Errors;

/// <summary>
/// Maps known exceptions to ProblemDetails with code and args (api.md "오류", quro-api step 5):
/// DomainException → 422, NotFoundException → 404, ConflictException and ConcurrencyException → 409, AuthException → AUTH-xxx status with localized title and detail. Other exceptions fall through to the default handler (500).
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AuthException auth)
        {
            return await AuthProblems.WriteAsync(httpContext, auth);
        }

        var (status, code, args) = exception switch
        {
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Args),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Args),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Args),
            ConcurrencyException => (StatusCodes.Status409Conflict, ConcurrencyException.Code, (IReadOnlyList<object>)[]),
            _ => (0, null, null),
        };
        if (code is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Extensions = { ["code"] = code, ["args"] = args },
            },
        });
    }
}

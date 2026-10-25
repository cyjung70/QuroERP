using Microsoft.AspNetCore.Antiforgery;

namespace QuroErp.Web.Common;

/// <summary>
/// 일반 폼을 받는 엔드포인트(RequireAntiforgeryTokenAttribute)의 위조 방지 토큰 검사 결과.
/// </summary>
public static class AntiforgeryCheck
{
    /// <summary>미들웨어 검사 결과를 본다. 폼 요청이 아니면 결과가 없어서 거부한다.</summary>
    public static bool IsValid(HttpContext context) =>
        context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: true };

    /// <summary>
    /// 400. 본문 없는 400은 UseStatusCodePagesWithReExecute가 /not-found로 다시 실행하고,
    /// 그 요청에서 위조 방지 검사가 또 실패해 500이 되므로 ProblemDetails 본문을 넣는다.
    /// </summary>
    public static IResult Rejected() => Results.Problem(statusCode: StatusCodes.Status400BadRequest);
}

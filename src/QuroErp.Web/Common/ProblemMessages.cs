using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using QuroErp.Contracts.Common;

namespace QuroErp.Web.Common;

/// <summary>메시지 코드(BC-E-001 등)를 사용자 언어의 문장으로 바꾼다 (i18n.md "UI 라벨·메시지").</summary>
public sealed class ProblemMessages(IStringLocalizer<SharedResource> localizer)
{
    /// <summary>리소스에 없는 코드나 코드 없는 오류에 보여주는 메시지.</summary>
    public const string UnknownCode = "Common-E-001";

    /// <summary>
    /// The message of an API error. A 403 whose code is missing or unknown shows the permission message Common-E-007, not
    /// Common-E-001 (i18n.md "공통 메시지").
    /// </summary>
    public string Get(ApiProblem problem) =>
        problem.Status == StatusCodes.Status403Forbidden && Find(problem.Code, [.. problem.Args]) is null
            ? localizer[ProblemCodes.Forbidden].Value
            : Get(problem.Code, [.. problem.Args]);

    public string Get(string? code, params object[] args) => Find(code, args) ?? localizer[UnknownCode].Value;

    private string? Find(string? code, object[] args)
    {
        if (code is null)
        {
            return null;
        }
        var message = localizer[code, args];
        return message.ResourceNotFound ? null : message.Value;
    }
}

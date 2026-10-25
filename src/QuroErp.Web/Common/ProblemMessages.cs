using Microsoft.Extensions.Localization;
using QuroErp.Contracts.Common;

namespace QuroErp.Web.Common;

/// <summary>메시지 코드(BC-E-001 등)를 사용자 언어의 문장으로 바꾼다 (i18n.md "UI 라벨·메시지").</summary>
public sealed class ProblemMessages(IStringLocalizer<SharedResource> localizer)
{
    /// <summary>리소스에 없는 코드나 코드 없는 오류에 보여주는 메시지.</summary>
    public const string UnknownCode = "Common-E-001";

    public string Get(ApiProblem problem) => Get(problem.Code, [.. problem.Args]);

    public string Get(string? code, params object[] args)
    {
        if (code is not null)
        {
            var message = localizer[code, args];
            if (!message.ResourceNotFound)
            {
                return message.Value;
            }
        }
        return localizer[UnknownCode].Value;
    }
}

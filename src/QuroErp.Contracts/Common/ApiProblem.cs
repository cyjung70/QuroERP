namespace QuroErp.Contracts.Common;

/// <summary>
/// API 오류 응답 (api.md "오류": RFC 7807 ProblemDetails, 확장 필드 code·args).
/// 메시지 문장은 보내지 않는다. Web이 code를 사용자 언어의 메시지로 바꾼다 (i18n.md).
/// </summary>
/// <param name="Code">메시지 코드 (예: BC-E-001). 본문이 ProblemDetails가 아니면 null.</param>
/// <param name="Args">메시지 인자 ({0}, {1} …).</param>
public sealed record ApiProblem(int Status, string? Code, IReadOnlyList<string> Args);

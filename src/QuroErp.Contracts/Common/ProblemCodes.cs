namespace QuroErp.Contracts.Common;

/// <summary>Message codes the API and the Web both name (i18n.md "공통 메시지").</summary>
public static class ProblemCodes
{
    /// <summary>403 without an account-state code: the caller may not do this (BC-BR-214, api.md "오류").</summary>
    public const string Forbidden = "Common-E-007";
}

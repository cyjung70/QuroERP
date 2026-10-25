namespace QuroErp.Contracts.Common.Codes;

/// <summary>Code API route (api.md "경로"): GET api/codes/{majorCode}?lang=ko.</summary>
public static class CodeRoutes
{
    public const string Base = "api/codes";
}

/// <summary>One code for a combo box or radio button. Code is the minor_code stored in business tables.</summary>
public sealed record CodeItemDto(string Code, string Name);

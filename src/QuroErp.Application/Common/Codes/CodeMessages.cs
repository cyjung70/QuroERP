namespace QuroErp.Application.Common.Codes;

/// <summary>Message codes for code validation and lookup.</summary>
public static class CodeMessages
{
    /// <summary>{0} = major code, {1} = value. Not an active Minor code.</summary>
    public const string InvalidCode = "Common-E-002";

    /// <summary>{0} = major code. The code group does not exist or is not in use.</summary>
    public const string UnknownMajor = "Common-E-003";
}

namespace QuroErp.Contracts.Common;

/// <summary>
/// Something the user should know about a save that still went through, e.g. BC-W-522. The screen shows the message for Code
/// with Args in the user's language.
/// </summary>
public sealed record ApiWarning(string Code, IReadOnlyList<string> Args);

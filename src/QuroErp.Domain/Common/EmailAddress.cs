using System.Text.RegularExpressions;

namespace QuroErp.Domain.Common;

/// <summary>Email format check shared by the field rules (BC-BR-102, BC-BR-303).</summary>
public static partial class EmailAddress
{
    public static bool IsValid(string value) => Format().IsMatch(value);

    // One @, no spaces, and a dot inside the domain part
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex Format();
}

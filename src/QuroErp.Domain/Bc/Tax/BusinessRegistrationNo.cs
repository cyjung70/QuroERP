using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Tax;

/// <summary>KR business registration number: 10 digits with a check digit (BC-BR-521).</summary>
public static class BusinessRegistrationNo
{
    /// <summary>Not 10 digits or wrong check digit. Arg: the value entered.</summary>
    public const string InvalidCode = "BC-E-521";

    private static readonly int[] Weights = [1, 3, 7, 1, 3, 7, 1, 3, 5];

    /// <summary>The 10 digits to store, without hyphens.</summary>
    /// <exception cref="DomainException">BC-E-521 when the value is not 10 digits or the check digit is wrong.</exception>
    public static string Normalize(string? value)
    {
        var digits = (value ?? "").Trim().Replace("-", "", StringComparison.Ordinal);
        return IsValid(digits) ? digits : throw new DomainException(InvalidCode, value ?? "");
    }

    /// <summary>10 digits (no hyphens) whose last digit matches the check digit.</summary>
    public static bool IsValid(string digits)
    {
        if (digits.Length != 10 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }
        var d = digits.Select(c => c - '0').ToArray();
        var sum = Weights.Select((w, i) => w * d[i]).Sum() + d[8] * 5 / 10;
        return (10 - sum % 10) % 10 == d[9];
    }
}

namespace QuroErp.Domain.Bc.Taxes;

/// <summary>KR business registration number (사업자등록번호, BC-BR-521).</summary>
public static class KrBusinessNumber
{
    private static readonly int[] Weights = [1, 3, 7, 1, 3, 7, 1, 3, 5];

    /// <summary>The number without hyphens, or null when it is not 10 digits with a matching check digit.</summary>
    public static string? Normalize(string? value)
    {
        var digits = value?.Replace("-", "", StringComparison.Ordinal).Trim();
        if (digits is not { Length: 10 } || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }
        var sum = 0;
        for (var i = 0; i < Weights.Length; i++)
        {
            sum += (digits[i] - '0') * Weights[i];
        }
        sum += (digits[8] - '0') * 5 / 10;
        return (10 - sum % 10) % 10 == digits[9] - '0' ? digits : null;
    }
}

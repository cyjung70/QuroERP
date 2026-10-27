using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Currency master row (bc_currency, BC-BR-411).</summary>
public sealed partial record Currency
{
    public const int MaxDecimals = 4;

    /// <summary>Invalid currency code or decimals. Arg: currency code.</summary>
    public const string InvalidCode = "BC-E-411";

    /// <exception cref="DomainException">BC-E-411 when the code is not 3 upper-case letters or decimals are outside 0~4.</exception>
    public Currency(string code, int decimals, bool inUse = true)
    {
        if (code is null || !CodePattern().IsMatch(code) || decimals is < 0 or > MaxDecimals)
        {
            throw new DomainException(InvalidCode, code ?? "");
        }
        Code = code;
        Decimals = decimals;
        InUse = inUse;
    }

    /// <summary>ISO 4217 code, e.g. USD.</summary>
    public string Code { get; }

    /// <summary>Amount decimals (USD 2, KRW 0).</summary>
    public int Decimals { get; }

    public bool InUse { get; }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodePattern();
}

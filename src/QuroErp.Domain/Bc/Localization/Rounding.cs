using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Rounding method of a module (sys_decimal_setting.rounding_mode, BC-BR-424).</summary>
public enum RoundingMode
{
    /// <summary>HALF_UP: half away from zero (default).</summary>
    HalfUp,

    /// <summary>DOWN: cut toward zero, e.g. Korean tax below one won.</summary>
    Down,
}

/// <summary>Rounding of computed values (localization.md "자릿수"). Only Domain rounds; repositories and screens do not.</summary>
public static class Rounding
{
    public const string HalfUpCode = "HALF_UP";
    public const string DownCode = "DOWN";

    /// <summary>Rounding mode is not HALF_UP or DOWN. Arg: the value.</summary>
    public const string InvalidModeCode = "BC-E-424";

    public static decimal Round(decimal value, int decimals, RoundingMode mode) =>
        Math.Round(value, decimals, mode == RoundingMode.Down ? MidpointRounding.ToZero : MidpointRounding.AwayFromZero);

    /// <summary>Number of digits after the decimal point, ignoring trailing zeros (1.50 → 1).</summary>
    public static int DecimalPlaces(decimal value)
    {
        var digits = 0;
        while (value != decimal.Truncate(value))
        {
            value *= 10;
            digits++;
        }
        return digits;
    }

    /// <exception cref="DomainException">BC-E-424 for any other value.</exception>
    public static RoundingMode ParseMode(string? code) => code switch
    {
        HalfUpCode => RoundingMode.HalfUp,
        DownCode => RoundingMode.Down,
        _ => throw new DomainException(InvalidModeCode, code ?? ""),
    };

    public static string ToCode(RoundingMode mode) => mode == RoundingMode.Down ? DownCode : HalfUpCode;
}

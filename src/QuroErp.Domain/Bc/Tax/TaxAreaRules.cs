using System.Globalization;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Tax;

/// <summary>A rate line as entered; either part may be missing (BC-BR-512).</summary>
public sealed record TaxAreaRateInput(DateOnly? EffectiveFrom, decimal? Rate);

/// <summary>A checked US tax area: trimmed upper-case code and state, and its rate lines in start date order (BC-BR-511, BC-BR-512).</summary>
public sealed record TaxAreaFields(string TaxAreaCode, string TaxAreaName, string StateCode, IReadOnlyList<TaxAreaRate> Rates);

/// <summary>US tax area rules (BC-BR-511, BC-BR-512). The rate on a document date is <see cref="TaxAreaRate.EffectiveOn"/> (BC-BR-513).</summary>
public static class TaxAreaRules
{
    /// <summary>
    /// The code is empty, too long or taken, or the name or state is missing (BC-BR-511). Also answers 409 when an area that tax codes use
    /// is deleted. Arg: tax area code as entered.
    /// </summary>
    public const string InvalidCode = "BC-E-511";

    /// <summary>A rate line has no start date, a rate outside 0 to under 100 or with more than 4 decimals, or a start date twice. Args: tax area code, start date.</summary>
    public const string InvalidRateCode = "BC-E-512";

    public const int CodeMaxLength = 20;

    public const int RateDecimals = 4;

    /// <exception cref="DomainException">BC-E-511 or BC-E-512.</exception>
    public static TaxAreaFields Normalize(string? code, string? name, string? stateCode, IEnumerable<TaxAreaRateInput> rates)
    {
        var cleanCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var cleanName = name?.Trim() ?? string.Empty;
        var cleanState = stateCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (cleanCode.Length is 0 or > CodeMaxLength || cleanName.Length == 0 || cleanState.Length == 0)
        {
            throw new DomainException(InvalidCode, code ?? string.Empty);
        }

        var lines = new List<TaxAreaRate>();
        foreach (var rate in rates)
        {
            if (rate.EffectiveFrom is not { } from || rate.Rate is not { } value || value < 0 || value >= 100
                || Rounding.DecimalPlaces(value) > RateDecimals || lines.Any(l => l.EffectiveFrom == from))
            {
                throw new DomainException(InvalidRateCode, cleanCode, Format(rate.EffectiveFrom));
            }
            lines.Add(new TaxAreaRate(from, value));
        }
        return new TaxAreaFields(cleanCode, cleanName, cleanState, [.. lines.OrderBy(l => l.EffectiveFrom)]);
    }

    /// <exception cref="DomainException">BC-E-511 when another area already has the code.</exception>
    public static void EnsureUnique(string code, bool takenByAnother)
    {
        if (takenByAnother)
        {
            throw new DomainException(InvalidCode, code);
        }
    }

    private static string Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

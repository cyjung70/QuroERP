using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>
/// Rounds amounts (net, tax, total, balance, clearing, discount) to the decimals of the document currency
/// (BC-BR-421). Amount decimals are never a module setting, so the same invoice rounds the same everywhere.
/// </summary>
public interface IAmountRounder
{
    /// <exception cref="DomainException">BC-E-412 when the currency is not in the currency master.</exception>
    int DecimalsOf(string currencyCode);

    /// <param name="mode">The module rounding mode for tax (BC-BR-424); other amounts use HALF_UP.</param>
    /// <exception cref="DomainException">BC-E-412 when the currency is not in the currency master.</exception>
    decimal Round(decimal amount, string currencyCode, RoundingMode mode = RoundingMode.HalfUp);
}

public sealed class AmountRounder : IAmountRounder
{
    /// <summary>Unknown or unused document currency. Arg: currency code.</summary>
    public const string UnknownCurrencyCode = "BC-E-412";

    private readonly Dictionary<string, int> _decimals;

    public AmountRounder(IEnumerable<Currency> currencies) =>
        _decimals = currencies.ToDictionary(c => c.Code, c => c.Decimals, StringComparer.Ordinal);

    public int DecimalsOf(string currencyCode) =>
        _decimals.TryGetValue(currencyCode, out var decimals) ? decimals : throw new DomainException(UnknownCurrencyCode, currencyCode);

    public decimal Round(decimal amount, string currencyCode, RoundingMode mode = RoundingMode.HalfUp) =>
        Rounding.Round(amount, DecimalsOf(currencyCode), mode);
}

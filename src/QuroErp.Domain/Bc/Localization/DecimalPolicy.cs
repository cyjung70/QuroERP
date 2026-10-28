using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Modules with their own unit price and quantity decimals (bc_country_decimal.module, sys_decimal_setting.module).</summary>
public static class DecimalModule
{
    public const string Mm = "MM";
    public const string Sd = "SD";

    public static IReadOnlyList<string> All { get; } = [Mm, Sd];
}

/// <summary>Quantity decimals and rounding mode of one module (sys_decimal_setting, BC-BR-422, BC-BR-424).</summary>
public sealed record DecimalSetting
{
    public const int MaxQtyDecimals = 4;

    /// <summary>Decimals outside the allowed range or unknown module. Arg: module.</summary>
    public const string InvalidCode = "BC-E-422";

    /// <exception cref="DomainException">BC-E-422 for an unknown module or decimals out of range.</exception>
    public DecimalSetting(string module, int qtyDecimals, RoundingMode roundingMode)
    {
        if (!DecimalModule.All.Contains(module) || qtyDecimals is < 0 or > MaxQtyDecimals)
        {
            throw new DomainException(InvalidCode, module ?? "");
        }
        Module = module;
        QtyDecimals = qtyDecimals;
        RoundingMode = roundingMode;
    }

    public string Module { get; }

    public int QtyDecimals { get; }

    public RoundingMode RoundingMode { get; }
}

/// <summary>Unit price decimals of one country and module (bc_country_decimal, BC-BR-422).</summary>
public sealed record CountryDecimal
{
    public const int MaxUnitPriceDecimals = 6;

    /// <exception cref="DomainException">BC-E-422 for an unknown module or decimals out of range.</exception>
    public CountryDecimal(string countryCode, string module, int unitPriceDecimals)
    {
        if (!DecimalModule.All.Contains(module) || unitPriceDecimals is < 0 or > MaxUnitPriceDecimals)
        {
            throw new DomainException(DecimalSetting.InvalidCode, module ?? "");
        }
        CountryCode = countryCode;
        Module = module;
        UnitPriceDecimals = unitPriceDecimals;
    }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string CountryCode { get; }

    public string Module { get; }

    public int UnitPriceDecimals { get; }
}

/// <summary>
/// Unit price and quantity decimals (BC-BR-422~424, BC-BR-429). Entered values that are too long are
/// rejected, not rounded; only computed values are rounded.
/// Unit price decimals come from the country row of the document currency, and from the company country row
/// when the currency has no country, that country has no row, or no currency is given (item standard price).
/// </summary>
public interface IDecimalPolicy
{
    /// <exception cref="DomainException">BC-E-422 when the module has no setting.</exception>
    DecimalSetting SettingOf(string moduleCode);

    /// <param name="currencyCode">Document currency; null for prices in the company base currency (item standard price).</param>
    /// <exception cref="DomainException">BC-E-422 when the company country has no row for the module.</exception>
    int UnitPriceDecimalsOf(string moduleCode, string? currencyCode);

    /// <summary>Checks an entered unit price.</summary>
    /// <param name="currencyCode">Document currency; null for the item standard price.</param>
    /// <param name="field">Field name for the message (e.g. unitPrice).</param>
    /// <exception cref="DomainException">BC-E-423 with field and allowed decimals when it has more decimals.</exception>
    void EnsureUnitPrice(string moduleCode, string? currencyCode, decimal value, string field);

    /// <summary>Checks an entered quantity.</summary>
    /// <exception cref="DomainException">BC-E-423 with field and allowed decimals when it has more decimals.</exception>
    void EnsureQty(string moduleCode, decimal value, string field);

    /// <summary>Rounds a computed unit price (e.g. moving average cost) with its decimals and the module rounding mode.</summary>
    decimal RoundUnitPrice(string moduleCode, string? currencyCode, decimal value);

    /// <summary>Rounds a computed quantity with the module decimals and rounding mode.</summary>
    decimal RoundQty(string moduleCode, decimal value);
}

public sealed class DecimalPolicy : IDecimalPolicy
{
    /// <summary>Entered value has more decimals than allowed. Args: field, allowed decimals.</summary>
    public const string TooManyDecimalsCode = "BC-E-423";

    private readonly string _companyCountryCode;
    private readonly Dictionary<string, DecimalSetting> _settings;
    private readonly Dictionary<(string Country, string Module), CountryDecimal> _countryDecimals;
    private readonly IReadOnlyDictionary<string, string?> _currencyCountries;

    /// <param name="companyCountryCode">bc_company.country_code.</param>
    /// <param name="currencyCountries">bc_currency.currency_code → country_code (null when the currency has no country).</param>
    public DecimalPolicy(
        string companyCountryCode, IEnumerable<DecimalSetting> settings, IEnumerable<CountryDecimal> countryDecimals,
        IReadOnlyDictionary<string, string?> currencyCountries)
    {
        _companyCountryCode = companyCountryCode;
        _settings = settings.ToDictionary(s => s.Module, StringComparer.Ordinal);
        _countryDecimals = countryDecimals.ToDictionary(c => (c.CountryCode, c.Module));
        _currencyCountries = currencyCountries;
    }

    public DecimalSetting SettingOf(string moduleCode) =>
        _settings.TryGetValue(moduleCode, out var setting) ? setting : throw new DomainException(DecimalSetting.InvalidCode, moduleCode);

    public int UnitPriceDecimalsOf(string moduleCode, string? currencyCode)
    {
        var currencyCountry = currencyCode is null ? null : _currencyCountries.GetValueOrDefault(currencyCode);
        if (currencyCountry is not null && _countryDecimals.TryGetValue((currencyCountry, moduleCode), out var row))
        {
            return row.UnitPriceDecimals;
        }
        return _countryDecimals.TryGetValue((_companyCountryCode, moduleCode), out var companyRow)
            ? companyRow.UnitPriceDecimals
            : throw new DomainException(DecimalSetting.InvalidCode, moduleCode);
    }

    public void EnsureUnitPrice(string moduleCode, string? currencyCode, decimal value, string field) =>
        Ensure(value, UnitPriceDecimalsOf(moduleCode, currencyCode), field);

    public void EnsureQty(string moduleCode, decimal value, string field) => Ensure(value, SettingOf(moduleCode).QtyDecimals, field);

    public decimal RoundUnitPrice(string moduleCode, string? currencyCode, decimal value) =>
        Rounding.Round(value, UnitPriceDecimalsOf(moduleCode, currencyCode), SettingOf(moduleCode).RoundingMode);

    public decimal RoundQty(string moduleCode, decimal value)
    {
        var setting = SettingOf(moduleCode);
        return Rounding.Round(value, setting.QtyDecimals, setting.RoundingMode);
    }

    private static void Ensure(decimal value, int decimals, string field)
    {
        if (Rounding.DecimalPlaces(value) > decimals)
        {
            throw new DomainException(TooManyDecimalsCode, field, decimals);
        }
    }
}

using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Modules with their own unit price and quantity decimals (sys_decimal_setting.module).</summary>
public static class DecimalModule
{
    public const string Mm = "MM";
    public const string Sd = "SD";

    public static IReadOnlyList<string> All { get; } = [Mm, Sd];
}

/// <summary>Unit price and quantity decimals and rounding mode of one module (sys_decimal_setting, BC-BR-422, BC-BR-424).</summary>
public sealed record DecimalSetting
{
    public const int MaxUnitPriceDecimals = 6;
    public const int MaxQtyDecimals = 4;

    /// <summary>Decimals outside the allowed range or unknown module. Arg: module.</summary>
    public const string InvalidCode = "BC-E-422";

    /// <exception cref="DomainException">BC-E-422 for an unknown module or decimals out of range.</exception>
    public DecimalSetting(string module, int unitPriceDecimals, int qtyDecimals, RoundingMode roundingMode)
    {
        if (!DecimalModule.All.Contains(module)
            || unitPriceDecimals is < 0 or > MaxUnitPriceDecimals
            || qtyDecimals is < 0 or > MaxQtyDecimals)
        {
            throw new DomainException(InvalidCode, module ?? "");
        }
        Module = module;
        UnitPriceDecimals = unitPriceDecimals;
        QtyDecimals = qtyDecimals;
        RoundingMode = roundingMode;
    }

    public string Module { get; }

    public int UnitPriceDecimals { get; }

    public int QtyDecimals { get; }

    public RoundingMode RoundingMode { get; }
}

/// <summary>
/// Module decimals for unit prices and quantities (BC-BR-422~424). Entered values that are too long are
/// rejected, not rounded; only computed values are rounded.
/// </summary>
public interface IDecimalPolicy
{
    /// <exception cref="DomainException">BC-E-422 when the module has no setting.</exception>
    DecimalSetting SettingOf(string moduleCode);

    /// <summary>Checks an entered unit price.</summary>
    /// <param name="field">Field name for the message (e.g. unitPrice).</param>
    /// <exception cref="DomainException">BC-E-423 with field and allowed decimals when it has more decimals.</exception>
    void EnsureUnitPrice(string moduleCode, decimal value, string field);

    /// <summary>Checks an entered quantity.</summary>
    /// <exception cref="DomainException">BC-E-423 with field and allowed decimals when it has more decimals.</exception>
    void EnsureQty(string moduleCode, decimal value, string field);

    /// <summary>Rounds a computed unit price (e.g. moving average cost) with the module decimals and rounding mode.</summary>
    decimal RoundUnitPrice(string moduleCode, decimal value);

    /// <summary>Rounds a computed quantity with the module decimals and rounding mode.</summary>
    decimal RoundQty(string moduleCode, decimal value);
}

public sealed class DecimalPolicy : IDecimalPolicy
{
    /// <summary>Entered value has more decimals than the module allows. Args: field, allowed decimals.</summary>
    public const string TooManyDecimalsCode = "BC-E-423";

    private readonly Dictionary<string, DecimalSetting> _settings;

    public DecimalPolicy(IEnumerable<DecimalSetting> settings) =>
        _settings = settings.ToDictionary(s => s.Module, StringComparer.Ordinal);

    public DecimalSetting SettingOf(string moduleCode) =>
        _settings.TryGetValue(moduleCode, out var setting) ? setting : throw new DomainException(DecimalSetting.InvalidCode, moduleCode);

    public void EnsureUnitPrice(string moduleCode, decimal value, string field) => Ensure(value, SettingOf(moduleCode).UnitPriceDecimals, field);

    public void EnsureQty(string moduleCode, decimal value, string field) => Ensure(value, SettingOf(moduleCode).QtyDecimals, field);

    public decimal RoundUnitPrice(string moduleCode, decimal value)
    {
        var setting = SettingOf(moduleCode);
        return Rounding.Round(value, setting.UnitPriceDecimals, setting.RoundingMode);
    }

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

using System.Text.RegularExpressions;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Currency fields after normalizing. Names are the sent changes by language code; null removes a name (BC-BR-405).</summary>
public sealed record CurrencyFields(int Decimals, string? CountryCode, bool UseYn, IReadOnlyDictionary<string, string?> Names);

/// <summary>A stored currency as the rules see it.</summary>
public sealed record StoredCurrency(string CurrencyCode, int Decimals, string? CountryCode);

/// <summary>A document or partner column that stores currency codes.</summary>
public sealed record CurrencyColumn(string Table, string Column);

/// <summary>Currency master rules (BC-BR-404, BC-BR-411, BC-BR-412, BC-BR-416, BC-BR-428, BC-BR-312). Every error names the currency code.</summary>
public static partial class CurrencyRules
{
    /// <summary>Invalid code or decimals (422), or decimals changed while documents use the currency (409). Arg: currency code.</summary>
    public const string InvalidCode = Currency.InvalidCode;

    /// <summary>The currency is not one in use (BC-BR-412). Arg: currency code.</summary>
    public const string NotInUseCode = "BC-E-412";

    /// <summary>Invalid country (422), or country changed while documents use the currency (409). Arg: currency code.</summary>
    public const string InvalidCountryCode = "BC-E-428";

    /// <summary>The en name is missing (BC-BR-404). Arg: currency code.</summary>
    public const string NameRequiredCode = "BC-E-404";

    /// <summary>The base currency cannot be set not in use (BC-BR-312). Arg: currency code.</summary>
    public const string BaseCurrencyCode = CompanyRules.BaseCurrencyCode;

    /// <summary>Default amount decimals for codes whose ISO 4217 minor unit is not 2.</summary>
    private static readonly IReadOnlyDictionary<string, int> IsoDecimals = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["BIF"] = 0,
        ["CLP"] = 0,
        ["DJF"] = 0,
        ["GNF"] = 0,
        ["ISK"] = 0,
        ["JPY"] = 0,
        ["KMF"] = 0,
        ["KRW"] = 0,
        ["PYG"] = 0,
        ["RWF"] = 0,
        ["UGX"] = 0,
        ["UYI"] = 0,
        ["VND"] = 0,
        ["VUV"] = 0,
        ["XAF"] = 0,
        ["XOF"] = 0,
        ["XPF"] = 0,
        ["BHD"] = 3,
        ["IQD"] = 3,
        ["JOD"] = 3,
        ["KWD"] = 3,
        ["LYD"] = 3,
        ["OMR"] = 3,
        ["TND"] = 3,
        ["CLF"] = 4,
        ["UYW"] = 4,
    };

    /// <summary>The base currency or a currency in use cannot be deleted (BC-BR-416). Arg: currency code.</summary>
    public const string InUseCode = "BC-E-416";

    /// <summary>
    /// Document columns that store a currency code (BC-BR-412). A currency stored in any of them is used by documents: its decimals
    /// and country cannot change and it cannot be deleted. Add a column here when a document table gets currency_code.
    /// bc_exchange_rate has no currency column yet; the task that adds one adds it to the delete check (BC-BR-416).
    /// </summary>
    public static readonly IReadOnlyList<CurrencyColumn> DocumentColumns =
    [
        new("sd_sales_order", "currency_code"),
        new("sd_shipment", "currency_code"),
        new("sd_sales_invoice", "currency_code"),
        new("sd_ar_receipt", "currency_code"),
        new("mm_purchase_order", "currency_code"),
        new("mm_goods_receipt", "currency_code"),
        new("mm_ap_payment", "currency_code"),
    ];

    /// <summary>Partner trade currencies (BC-BR-415). A currency stored here cannot be deleted; its decimals and country may change.</summary>
    public static readonly IReadOnlyList<CurrencyColumn> PartnerColumns = [new("bc_customer", "currency_code"), new("bc_vendor", "currency_code")];

    /// <summary>BC-BR-411: trimmed upper-case ISO 4217 code.</summary>
    /// <exception cref="DomainException">BC-E-411 when it is not 3 letters.</exception>
    public static string NormalizeCode(string? currencyCode) => new Currency(currencyCode?.Trim().ToUpperInvariant() ?? "", 0).Code;

    /// <summary>BC-BR-411: the ISO 4217 minor unit, 2 for codes not listed.</summary>
    public static int DefaultDecimals(string currencyCode) => IsoDecimals.GetValueOrDefault(currencyCode, 2);

    /// <summary>
    /// BC-BR-404, 411, 428 for one row. Decimals default to the ISO 4217 value, the country is ISO 3166-1 alpha-2 or empty,
    /// a missing in-use flag means in use and the en name is required.
    /// </summary>
    public static CurrencyFields Normalize(
        string currencyCode, int? decimals, string? countryCode, bool? useYn, IReadOnlyDictionary<string, string?>? names)
    {
        var amountDecimals = new Currency(currencyCode, decimals ?? DefaultDecimals(currencyCode)).Decimals;
        var country = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        if (country is not null && !CountryPattern().IsMatch(country))
        {
            throw new DomainException(InvalidCountryCode, currencyCode);
        }
        var kept = CodeNames.Normalize(names, NameRequiredCode, currencyCode);
        return new CurrencyFields(amountDecimals, country, useYn ?? true, kept);
    }

    /// <summary>A stored currency code never changes. A blank code in a change keeps the stored one.</summary>
    /// <exception cref="DomainException">BC-E-411 with the stored code when another code is sent.</exception>
    public static void EnsureSameCode(string storedCode, string? requestedCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode) && !string.Equals(requestedCode.Trim(), storedCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(InvalidCode, storedCode);
        }
    }

    /// <summary>BC-BR-411: currency codes are unique among the currencies the save leaves.</summary>
    /// <exception cref="DomainException">BC-E-411 with the repeated code.</exception>
    public static void EnsureUnique(IEnumerable<string> currencyCodes)
    {
        if (currencyCodes.GroupBy(c => c, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new DomainException(InvalidCode, repeated.Key);
        }
    }

    /// <summary>
    /// BC-BR-411, 428: the error code (for a 409) when the change touches what documents fix — decimals first, then country —
    /// or null when the change is allowed.
    /// </summary>
    public static string? LockedByDocuments(StoredCurrency stored, CurrencyFields fields) =>
        fields.Decimals != stored.Decimals ? InvalidCode
        : !string.Equals(fields.CountryCode, stored.CountryCode, StringComparison.Ordinal) ? InvalidCountryCode
        : null;

    /// <summary>
    /// BC-BR-416: the first deleted code that is the base currency or that documents or partners use, for a 409 BC-E-416, or null
    /// when every deleted currency may go. Such a currency is set not in use instead.
    /// </summary>
    public static string? FirstUndeletable(IEnumerable<string> deletedCodes, string? baseCurrencyCode, IReadOnlySet<string> usedCodes) =>
        deletedCodes.FirstOrDefault(code => code == baseCurrencyCode || usedCodes.Contains(code));

    /// <summary>BC-BR-312: the company base currency stays in use.</summary>
    /// <exception cref="DomainException">BC-E-312 with the code.</exception>
    public static void EnsureBaseStaysInUse(string currencyCode, bool useYn, string? baseCurrencyCode)
    {
        if (!useYn && currencyCode == baseCurrencyCode)
        {
            throw new DomainException(BaseCurrencyCode, currencyCode);
        }
    }

    /// <summary>BC-BR-412: a document currency must be a currency in use.</summary>
    /// <exception cref="DomainException">BC-E-412 with the code.</exception>
    public static void EnsureUsable(string currencyCode, IEnumerable<Currency> currencies)
    {
        if (!currencies.Any(c => c.InUse && c.Code == currencyCode))
        {
            throw new DomainException(NotInUseCode, currencyCode);
        }
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryPattern();
}

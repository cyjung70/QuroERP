using System.Text.RegularExpressions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Domain.Bc.Tax;

/// <summary>
/// Tax code fields after normalizing. Rate is a percentage (10 = 10%), 0 unless the kind is VAT_STANDARD; SALES_TAX takes its rate
/// from TaxAreaId (BC-BR-503). Names are the sent changes by language code; null removes a name (BC-BR-405).
/// </summary>
public sealed record TaxCodeFields(
    string TaxKind, decimal Rate, long? TaxAreaId, bool PriceIncluded, bool UseYn, IReadOnlyDictionary<string, string?> Names);

/// <summary>A stored tax code as the rules see it.</summary>
public sealed record StoredTaxCode(string TaxCode, string TaxKind, decimal Rate, long? TaxAreaId);

/// <summary>A document or partner column that stores tax code ids.</summary>
public sealed record TaxCodeColumn(string Table, string Column);

/// <summary>Tax code master rules (BC-BR-501~506, BC-BR-404). Every error names the tax code.</summary>
public static partial class TaxCodeRules
{
    /// <summary>The code is missing, not up to 10 upper-case letters or digits, or repeated (BC-BR-501). Arg: tax code.</summary>
    public const string InvalidCode = "BC-E-501";

    /// <summary>The kind is not one of the company country (BC-BR-502). Arg: tax code.</summary>
    public const string KindCode = "BC-E-502";

    /// <summary>The rate is not 0 or more and under 100 with up to 4 decimals, or SALES_TAX has no known tax area (BC-BR-503). Arg: tax code.</summary>
    public const string RateCode = "BC-E-503";

    /// <summary>Price included on a kind other than VAT_STANDARD (BC-BR-504). Arg: tax code.</summary>
    public const string PriceIncludedCode = "BC-E-504";

    /// <summary>NONE is deleted, set not in use, renamed or given another kind (BC-BR-505). Arg: tax code.</summary>
    public const string NoneCode = "BC-E-505";

    /// <summary>
    /// A code documents use is deleted or its rate, kind or tax area changes, or a code partners use is deleted (BC-BR-506).
    /// Arg: tax code.
    /// </summary>
    public const string InUseCode = "BC-E-506";

    /// <summary>The en name is missing (BC-BR-404). Arg: tax code.</summary>
    public const string NameRequiredCode = CurrencyRules.NameRequiredCode;

    /// <summary>The code every country has (BC-BR-505); migrated document lines use it.</summary>
    public const string NoneTaxCode = "NONE";

    /// <summary>The VAT_STANDARD rate when none is entered (BC-BR-503).</summary>
    public const decimal DefaultVatRate = 10m;

    /// <summary>Decimals a rate may have (BC-BR-503).</summary>
    public const int RateDecimals = 4;

    /// <summary>Document line columns that store a tax code id. A code stored in any of them is used by documents (BC-BR-506).</summary>
    public static readonly IReadOnlyList<TaxCodeColumn> DocumentColumns =
    [
        new("mm_purchase_order_line", "tax_code_id"),
        new("mm_goods_receipt_line", "tax_code_id"),
        new("sd_sales_order_line", "tax_code_id"),
        new("sd_sales_invoice_line", "tax_code_id"),
    ];

    /// <summary>Partner default tax codes. A code stored here cannot be deleted; its rate and kind may change.</summary>
    public static readonly IReadOnlyList<TaxCodeColumn> PartnerColumns = [new("bc_customer", "tax_code_id"), new("bc_vendor", "tax_code_id")];

    /// <summary>BC-BR-501: trimmed upper-case code of 1~10 letters or digits.</summary>
    /// <exception cref="DomainException">BC-E-501 with the code as sent.</exception>
    public static string NormalizeCode(string? taxCode)
    {
        var code = taxCode?.Trim().ToUpperInvariant() ?? "";
        return CodePattern().IsMatch(code) ? code : throw new DomainException(InvalidCode, code);
    }

    /// <summary>
    /// BC-BR-502~504 and 404 for one row. The kind must be one of the country's; VAT_STANDARD keeps its rate (10 when empty),
    /// SALES_TAX needs a known tax area, and every other kind has rate 0 and no area. A missing in-use flag means in use.
    /// </summary>
    /// <param name="countryKinds">Tax kinds of the company country (ICountryProfile.TaxKinds).</param>
    /// <param name="knownTaxAreas">Tax area ids that exist.</param>
    public static TaxCodeFields Normalize(
        string taxCode, string? taxKind, decimal? rate, long? taxAreaId, bool? priceIncluded, bool? useYn,
        IReadOnlyDictionary<string, string?>? names, IReadOnlyCollection<string> countryKinds, IReadOnlySet<long> knownTaxAreas)
    {
        var kind = taxKind?.Trim().ToUpperInvariant() ?? "";
        if (!countryKinds.Contains(kind))
        {
            throw new DomainException(KindCode, taxCode);
        }
        var storedRate = 0m;
        long? area = null;
        if (kind == TaxKind.VatStandard)
        {
            storedRate = rate ?? DefaultVatRate;
            if (storedRate is < 0 or >= 100 || Rounding.DecimalPlaces(storedRate) > RateDecimals)
            {
                throw new DomainException(RateCode, taxCode);
            }
        }
        else if (kind == TaxKind.SalesTax)
        {
            area = taxAreaId is { } id && knownTaxAreas.Contains(id) ? id : throw new DomainException(RateCode, taxCode);
        }
        if (priceIncluded == true && kind != TaxKind.VatStandard)
        {
            throw new DomainException(PriceIncludedCode, taxCode);
        }
        var kept = CodeNames.Normalize(names, NameRequiredCode, taxCode);
        return new TaxCodeFields(kind, storedRate, area, priceIncluded ?? false, useYn ?? true, kept);
    }

    /// <summary>BC-BR-501: codes are unique among the tax codes the save leaves.</summary>
    /// <exception cref="DomainException">BC-E-501 with the repeated code.</exception>
    public static void EnsureUnique(IEnumerable<string> taxCodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in taxCodes)
        {
            if (!seen.Add(code))
            {
                throw new DomainException(InvalidCode, code);
            }
        }
    }

    /// <summary>BC-BR-505: the change renames NONE, gives it another kind or sets it not in use (409 BC-E-505).</summary>
    public static bool ChangesNone(string storedCode, string newCode, TaxCodeFields fields) =>
        storedCode == NoneTaxCode && (newCode != NoneTaxCode || fields.TaxKind != TaxKind.None || !fields.UseYn);

    /// <summary>BC-BR-506: a code documents use keeps its kind, rate and tax area (a new rate needs a new code).</summary>
    public static bool ChangesWhatDocumentsUse(StoredTaxCode stored, TaxCodeFields fields) =>
        stored.TaxKind != fields.TaxKind || stored.Rate != fields.Rate || stored.TaxAreaId != fields.TaxAreaId;

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();
}

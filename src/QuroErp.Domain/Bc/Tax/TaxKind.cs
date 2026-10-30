namespace QuroErp.Domain.Bc.Tax;

/// <summary>Tax kind of a tax code (bc_tax_code.tax_kind, tax.md "세금 코드", BC-BR-502).</summary>
public static class TaxKind
{
    /// <summary>US: combined rate of the tax area.</summary>
    public const string SalesTax = "SALES_TAX";

    /// <summary>US: exempt (exemption certificate, resale).</summary>
    public const string SalesTaxExempt = "SALES_TAX_EXEMPT";

    /// <summary>KR: taxable, entered rate (default 10%).</summary>
    public const string VatStandard = "VAT_STANDARD";

    /// <summary>KR: zero rate (exports).</summary>
    public const string VatZero = "VAT_ZERO";

    /// <summary>KR: exempt.</summary>
    public const string VatExempt = "VAT_EXEMPT";

    /// <summary>Not subject to tax (migrated data). Every country has it.</summary>
    public const string None = "NONE";

    public static IReadOnlyList<string> All { get; } = [SalesTax, SalesTaxExempt, VatStandard, VatZero, VatExempt, None];
}

/// <summary>A tax code installed for a country (BC-BR-505). Rate is a percentage (10 = 10%).</summary>
public sealed record DefaultTaxCode(string Code, string Kind, decimal Rate);

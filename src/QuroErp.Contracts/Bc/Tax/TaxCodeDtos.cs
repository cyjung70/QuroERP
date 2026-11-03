namespace QuroErp.Contracts.Bc.Tax;

/// <summary>Tax code API routes (BC-UC-028).</summary>
public static class TaxCodeRoutes
{
    /// <summary>GET ?lang=&amp;useYn=&amp;taxKind= (every tax code in code order).</summary>
    public const string Base = "api/bc/tax-codes";

    /// <summary>POST the grid changes in one transaction (screens.md "유형별 저장 API 형태").</summary>
    public const string Batch = Base + "/batch";
}

/// <summary>A tax code (bc_tax_code) with its names (bc_tax_code_translation).</summary>
/// <param name="Name">Name in the requested language, falling back to the company default language, then en (BC-BR-402).</param>
/// <param name="Names">Name by language code, for every language that has one. en is always there (BC-BR-404).</param>
/// <param name="Rate">Percentage (10 = 10%); 0 except VAT_STANDARD. SALES_TAX takes the rate of its tax area (BC-BR-503).</param>
/// <param name="TaxAreaCode">Code of the tax area of a SALES_TAX code, or null.</param>
/// <param name="InUseByDocuments">A document line stores the code: rate, kind and tax area cannot change, and it cannot be deleted (BC-BR-506).</param>
public sealed record TaxCodeDto(
    long Id, string TaxCode, string? Name, IReadOnlyDictionary<string, string> Names, string TaxKind, decimal Rate, long? TaxAreaId,
    string? TaxAreaCode, bool PriceIncluded, bool UseYn, bool InUseByDocuments, DateTimeOffset UpdatedAt);

/// <summary>
/// A new or changed tax code row. A new row has no Id. A changed row sends the Id and the updatedAt it read (409 when stale).
/// </summary>
/// <param name="Rate">VAT_STANDARD only; null means 10 (BC-BR-503). Ignored for other kinds.</param>
/// <param name="TaxAreaId">SALES_TAX only, required (BC-BR-503). Ignored for other kinds.</param>
/// <param name="PriceIncluded">VAT_STANDARD only (BC-BR-504). Null means false.</param>
/// <param name="UseYn">Null means in use.</param>
/// <param name="Names">Name changes by language; en is required, a blank name removes that language (BC-BR-404, BC-BR-405).</param>
public sealed record TaxCodeSaveRow(
    long? Id, string? TaxCode, string? TaxKind, decimal? Rate, long? TaxAreaId, bool? PriceIncluded, bool? UseYn,
    IReadOnlyDictionary<string, string?>? Names, DateTimeOffset? UpdatedAt);

/// <summary>A deleted tax code in a batch: its Id and the updatedAt it read.</summary>
public sealed record TaxCodeKey(long Id, DateTimeOffset UpdatedAt);

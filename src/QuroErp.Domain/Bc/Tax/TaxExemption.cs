namespace QuroErp.Domain.Bc.Tax;

/// <summary>US customer exemption certificate (sd_customer_tax_exemption, BC-BR-524). ValidTo null = no expiry.</summary>
public sealed record TaxExemptionCertificate(string CertificateNo, string StateCode, DateOnly? ValidTo)
{
    public bool IsValidOn(DateOnly date) => ValidTo is null || ValidTo >= date;
}

/// <summary>Tax code default of an invoice from the exemption certificates of its ship-to state (BC-BR-525).</summary>
/// <param name="Exempt">Use the SALES_TAX_EXEMPT code; otherwise the customer's default tax code.</param>
/// <param name="CertificateNo">Certificate number to keep on the invoice when exempt.</param>
/// <param name="Warning">BC-W-525 when the state has only expired certificates.</param>
public sealed record TaxExemptionDefault(bool Exempt, string? CertificateNo, string? Warning)
{
    /// <summary>Only expired certificates for the state; taxed with the customer's default code. No args.</summary>
    public const string ExpiredWarning = "BC-W-525";

    public static TaxExemptionDefault For(IEnumerable<TaxExemptionCertificate> certificates, string stateCode, DateOnly invoiceDate)
    {
        var ofState = certificates.Where(c => string.Equals(c.StateCode, stateCode, StringComparison.Ordinal)).ToList();
        // No expiry counts as the latest
        var valid = ofState.Where(c => c.IsValidOn(invoiceDate)).MaxBy(c => c.ValidTo ?? DateOnly.MaxValue);
        if (valid is not null)
        {
            return new TaxExemptionDefault(true, valid.CertificateNo, null);
        }
        return ofState.Count > 0 ? new TaxExemptionDefault(false, null, ExpiredWarning) : new TaxExemptionDefault(false, null, null);
    }
}

using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Sd.Customers;

/// <summary>US customer exemption certificates as the customer screen grid saves them (SD-BR-333, BC-BR-524).</summary>
public static class CustomerTaxExemptions
{
    /// <summary>Certificate number or state missing, or two valid certificates for one state. Arg: property name or state.</summary>
    public const string InvalidCode = "BC-E-524";

    /// <summary>A changed or deleted row that is not a certificate of this customer. Arg: row id.</summary>
    public const string NotOfCustomerCode = "SD-E-333";

    public const int CertificateNoMaxLength = 50;

    /// <summary>Trimmed certificate number and upper case state; both are required (BC-BR-524).</summary>
    /// <exception cref="DomainException">BC-E-524, SD-E-314.</exception>
    public static TaxExemptionCertificate Clean(string? certificateNo, string? stateCode, DateOnly? validTo)
    {
        var number = string.IsNullOrWhiteSpace(certificateNo) ? null : certificateNo.Trim();
        var state = string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim().ToUpperInvariant();
        if (number is null)
        {
            throw new DomainException(InvalidCode, nameof(TaxExemptionCertificate.CertificateNo));
        }
        if (state is null)
        {
            throw new DomainException(InvalidCode, nameof(TaxExemptionCertificate.StateCode));
        }
        if (number.Length > CertificateNoMaxLength)
        {
            throw new DomainException(Customer.InvalidTextCode, nameof(TaxExemptionCertificate.CertificateNo), CertificateNoMaxLength);
        }
        return new TaxExemptionCertificate(number, state, validTo);
    }

    /// <summary>
    /// After the save, a customer has at most one certificate valid on <paramref name="today"/> per state (BC-BR-524). Expired
    /// certificates are kept as history.
    /// </summary>
    /// <exception cref="DomainException">BC-E-524 with the state.</exception>
    public static void EnsureOneValidPerState(IEnumerable<TaxExemptionCertificate> certificates, DateOnly today)
    {
        var state = certificates.Where(c => c.IsValidOn(today)).GroupBy(c => c.StateCode).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (state is not null)
        {
            throw new DomainException(InvalidCode, state);
        }
    }
}

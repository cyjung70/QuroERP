using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>An exemption certificate row of a US customer on the customer master screen (BC-BR-524).</summary>
public sealed class CustomerTaxExemptionRow
{
    public long Id { get; set; }
    public string? CertificateNo { get; set; }

    /// <summary>A STATE code; the grid keeps it in upper case.</summary>
    public string? StateCode { get; set; }

    /// <summary>Empty means no expiry.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static CustomerTaxExemptionRow From(CustomerTaxExemptionDto dto) =>
        new() { Id = dto.Id, CertificateNo = dto.CertificateNo, StateCode = dto.StateCode, ValidTo = dto.ValidTo, UpdatedAt = dto.UpdatedAt };

    /// <summary>Grid changes as one batch request. Loaded rows always have updatedAt; the API normalizes and checks every value.</summary>
    public static BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey> ToRequest(BatchChanges<CustomerTaxExemptionRow> changes) =>
        new(
            [.. changes.Added.Select(r => new CustomerTaxExemptionSaveRow(null, r.CertificateNo, r.StateCode, r.ValidTo, null))],
            [.. changes.Updated.Select(r => new CustomerTaxExemptionSaveRow(r.Id, r.CertificateNo, r.StateCode, r.ValidTo, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new CustomerTaxExemptionKey(r.Id, r.UpdatedAt!.Value))]);
}

using System.Data;

namespace QuroErp.Application.Mm.Labels;

/// <summary>The item a scanned value names.</summary>
public sealed record LabelScanItem(long Id, string ItemCd, string? ItemName);

/// <summary>Reads of a scan (MM-BR-923).</summary>
public interface ILabelScanRepository
{
    /// <summary>The item with this barcode (upc_code), or null.</summary>
    Task<LabelScanItem?> FindItemByBarcodeAsync(string upcCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The item with this item code, or null.</summary>
    Task<LabelScanItem?> FindItemByCodeAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken);
}

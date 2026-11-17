using System.Data;
using QuroErp.Application.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>
/// Two items for the scan API tests: CABLE with barcode 880000000017 and BOLT whose code is also another item's barcode, so the
/// barcode wins (MM-BR-923). The SQL is covered by LabelScanRepositoryTests on real PostgreSQL.
/// </summary>
public sealed class InMemoryLabelScanRepository : ILabelScanRepository
{
    public static readonly LabelScanItem Cable = new(501, "CABLE", "Cable Reel");
    public static readonly LabelScanItem Bolt = new(502, "BOLT", "Hex Bolt");

    private static readonly Dictionary<string, LabelScanItem> Barcodes = new(StringComparer.Ordinal)
    {
        ["880000000017"] = Cable,
        ["BOLT"] = Cable,
    };

    public Task<LabelScanItem?> FindItemByBarcodeAsync(string upcCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Barcodes.GetValueOrDefault(upcCode));

    public Task<LabelScanItem?> FindItemByCodeAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(new[] { Cable, Bolt }.FirstOrDefault(i => i.ItemCd == itemCd));
}

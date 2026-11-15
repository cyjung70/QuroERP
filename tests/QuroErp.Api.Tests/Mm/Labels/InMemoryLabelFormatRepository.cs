using System.Data;
using QuroErp.Application.Mm.Labels;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>bc_label_format in memory with the four default formats of the install seed (MM-BR-917).</summary>
public sealed class InMemoryLabelFormatRepository : ILabelFormatRepository
{
    public static readonly DateTimeOffset Stored = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<LabelFormatRow> _rows =
    [
        new(new(LabelType.Pallet, LabelType.Zpl, 100, 150, 203, ["palletNo", "itemCd", "itemName", "lotNo", "qty", "expiryDt"]), true, Stored),
        new(new(LabelType.Item, LabelType.Zpl, 50, 25, 203, ["itemCd", "itemName"]), true, Stored),
        new(new(LabelType.Lot, LabelType.Zpl, 50, 25, 203, ["itemCd", "lotNo", "expiryDt"]), true, Stored),
        new(new(LabelType.Location, LabelType.Zpl, 100, 50, 203, ["warehouseCode", "locationCode"]), true, Stored),
    ];

    public Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.FirstOrDefault(r => r.Format.LabelType == labelType && r.UseYn)?.Format);
        }
    }

    public Task<IReadOnlyList<LabelFormatRow>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LabelFormatRow>>([.. _rows]);
        }
    }

    public Task<IReadOnlyList<LabelFormatRow>> LockAsync(
        IReadOnlyCollection<string> labelTypes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LabelFormatRow>>([.. _rows.Where(r => labelTypes.Contains(r.Format.LabelType))]);
        }
    }

    public Task UpdateAsync(LabelFormat format, bool useYn, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var index = _rows.FindIndex(r => r.Format.LabelType == format.LabelType);
            _rows[index] = new LabelFormatRow(format, useYn, _rows[index].UpdatedAt.AddSeconds(1));
        }
        return Task.CompletedTask;
    }
}

/// <summary>A label PDF without Chromium: the PDF header and the number of pages.</summary>
public sealed class FakeLabelPdfRenderer : ILabelPdfRenderer
{
    public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken) =>
        Task.FromResult(System.Text.Encoding.ASCII.GetBytes($"%PDF-fake pages={pages.Count * copies}"));
}

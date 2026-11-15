using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>Label formats (MM-UC-036): the four formats, saving changes to them and a sample PDF of each (MM-BR-917, MM-BR-918).</summary>
public sealed class LabelFormatService(ILabelFormatRepository formats, ILabelPdfRenderer pdf, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>No label type of this name. Arg: the label type.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>The four formats in label type order.</summary>
    public async Task<IReadOnlyList<LabelFormatDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await formats.ListAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(ToDto)];
    }

    /// <summary>
    /// Saves the changed formats in one transaction and returns the four formats after the save. One failing row saves nothing.
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 MM-E-917 (label type) when a row is added or deleted, a label type is unknown or named twice, or a format breaks
    /// MM-BR-917.
    /// </exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a format changed after it was read.</exception>
    public async Task<IReadOnlyList<LabelFormatDto>> SaveAsync(
        BatchRequest<LabelFormatSaveRow, LabelFormatKey> request, string updatedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The four formats are made at install and only change (MM-BR-917)
        if (request.Added?.Count > 0)
        {
            throw new DomainException(LabelFormatRules.InvalidCode, request.Added[0].LabelType ?? "");
        }
        if (request.Deleted?.Count > 0)
        {
            throw new DomainException(LabelFormatRules.InvalidCode, request.Deleted[0].LabelType ?? "");
        }
        IReadOnlyList<LabelFormatSaveRow> updated = request.Updated ?? [];
        var types = updated.Select(r => r.LabelType?.Trim().ToUpperInvariant() ?? "").ToList();
        var unknownOrRepeated = types.Where((t, i) => !LabelFormatRules.IsType(t) || types.IndexOf(t) != i).FirstOrDefault();
        if (unknownOrRepeated is not null)
        {
            throw new DomainException(LabelFormatRules.InvalidCode, unknownOrRepeated);
        }
        var changed = updated.Select((row, i) => (Row: row, Format: LabelFormatRules.Validate(new LabelFormat(
            types[i], row.OutputKind ?? "", row.WidthMm ?? 0, row.HeightMm ?? 0, row.Dpi ?? 0, row.Fields ?? [])))).ToList();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await formats.LockAsync(types, uow.Transaction, cancellationToken))
            .ToDictionary(r => r.Format.LabelType, StringComparer.Ordinal);
        // Rows the save names must still be as they were read; otherwise someone else saved first
        if (changed.Any(c => !stored.TryGetValue(c.Format.LabelType, out var row) || row.UpdatedAt != c.Row.UpdatedAt))
        {
            throw new ConcurrencyException();
        }
        foreach (var (row, format) in changed)
        {
            await formats.UpdateAsync(format, row.UseYn ?? true, updatedBy, uow.Transaction, cancellationToken);
        }
        var after = await formats.ListAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. after.Select(ToDto)];
    }

    /// <summary>
    /// A sample label of the type as a PDF of the stored format's size and fields, in use or not, whatever its output kind. A
    /// Pallet gives its label page, then its DataMatrix page (MM-BR-918).
    /// </summary>
    /// <exception cref="NotFoundException">404 Common-E-006 for an unknown label type.</exception>
    public async Task<byte[]> PreviewAsync(string labelType, CancellationToken cancellationToken)
    {
        var type = labelType?.Trim().ToUpperInvariant() ?? "";
        if (!LabelFormatRules.IsType(type))
        {
            throw new NotFoundException(NotFoundCode, labelType ?? "");
        }
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = (await formats.ListAsync(uow.Transaction, cancellationToken)).FirstOrDefault(r => r.Format.LabelType == type)
            ?? throw new NotFoundException(NotFoundCode, type);
        await uow.CommitAsync(cancellationToken);
        return await pdf.RenderAsync(row.Format, LabelPages.For([LabelFormatRules.Sample(type)]), 1, cancellationToken);
    }

    private static LabelFormatDto ToDto(LabelFormatRow r) =>
        new(r.Format.LabelType, r.Format.OutputKind, r.Format.WidthMm, r.Format.HeightMm, r.Format.Dpi, r.Format.Fields,
            LabelFormatRules.FieldsOf(r.Format.LabelType), r.UseYn, r.UpdatedAt);
}

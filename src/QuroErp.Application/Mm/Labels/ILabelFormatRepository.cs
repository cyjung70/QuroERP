using System.Data;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>A stored label format with its use flag and when it was last saved.</summary>
public sealed record LabelFormatRow(LabelFormat Format, bool UseYn, DateTimeOffset UpdatedAt);

/// <summary>Label formats (bc_label_format, MM-BR-917).</summary>
public interface ILabelFormatRepository
{
    /// <summary>The format of the label type when it is in use, else null.</summary>
    Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Every format, in use or not, in the order of <see cref="LabelFormatRules.Types"/>.</summary>
    Task<IReadOnlyList<LabelFormatRow>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The formats of the label types, locked (FOR UPDATE) in label type order.</summary>
    Task<IReadOnlyList<LabelFormatRow>> LockAsync(
        IReadOnlyCollection<string> labelTypes, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the output kind, size, dpi, fields and use flag of a locked format.</summary>
    Task UpdateAsync(LabelFormat format, bool useYn, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);
}

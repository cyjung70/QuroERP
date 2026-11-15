using System.Data;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>Label formats (bc_label_format, MM-BR-917).</summary>
public interface ILabelFormatRepository
{
    /// <summary>The format of the label type when it is in use, else null.</summary>
    Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken);
}
